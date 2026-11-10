module Ballast.Analysis.SymbolExtractor

open System
open System.Collections.Generic
open System.IO
open FSharp.Compiler.CodeAnalysis
open FSharp.Compiler.Symbols
open FSharp.Compiler.Text
open Ballast.Core
open Ballast.Analysis.ProjectLoader

let private isAnalysedFile (path: string) =
    let inGeneratedFolder =
        path.Split([| '\\'; '/' |]) |> Array.exists (fun part -> part.Equals("obj", StringComparison.OrdinalIgnoreCase))

    path.EndsWith ".fs" && not inGeneratedFolder

let private keyOf (entity: FSharpEntity) =
    { Assembly = entity.Assembly.SimpleName
      Path = entity.AccessPath
      Name = entity.CompiledName }

/// The abbreviation's own declaration carries the edge to its target, so abbreviations are not expanded.
let rec private namedTypesIn (fsharpType: FSharpType) : FSharpEntity list =
    if fsharpType.IsGenericParameter then
        []
    else
        [ if fsharpType.HasTypeDefinition then
              fsharpType.TypeDefinition

          for argument in fsharpType.GenericArguments do
              yield! namedTypesIn argument ]

let private targetsOf (symbolUse: FSharpSymbolUse) : FSharpEntity list =
    match symbolUse.Symbol with
    | :? FSharpEntity as entity when not entity.IsNamespace -> [ entity ]
    | :? FSharpMemberOrFunctionOrValue as value ->
        [ yield! Option.toList value.DeclaringEntity

          // Inferred types leave no symbol use in the source, only a signature.
          if symbolUse.IsFromDefinition then
              yield! namedTypesIn value.FullType ]
    | :? FSharpUnionCase as unionCase -> [ unionCase.DeclaringEntity ]
    | :? FSharpField as field -> Option.toList field.DeclaringEntity
    | :? FSharpActivePatternCase as case -> Option.toList case.Group.DeclaringEntity
    | _ -> []

let private lastSegment (name: string) = name.Substring(name.LastIndexOf '.' + 1)

let rec private namespaceOf (entity: FSharpEntity) =
    match entity.Namespace, entity.DeclaringEntity with
    | Some path, _ -> path
    | None, Some parent -> namespaceOf parent
    | None, None -> "<global>"

let private fullModulePath (entity: FSharpEntity) =
    if entity.AccessPath = "global" then
        entity.DisplayName
    else
        entity.AccessPath + "." + entity.DisplayName

let rec private modulePathOf (entity: FSharpEntity) =
    if entity.IsFSharpModule then
        Some(fullModulePath entity)
    else
        entity.DeclaringEntity |> Option.bind modulePathOf

let rec private withoutAbbreviations (fsharpType: FSharpType) =
    if fsharpType.IsAbbreviation then
        withoutAbbreviations fsharpType.AbbreviatedType
    else
        fsharpType

let private definitionName (fsharpType: FSharpType) =
    let fsharpType = withoutAbbreviations fsharpType

    if fsharpType.HasTypeDefinition then
        fsharpType.TypeDefinition.AccessPath + "." + fsharpType.TypeDefinition.CompiledName
    else
        ""

let private asyncTypes =
    set
        [ "Microsoft.FSharp.Control.FSharpAsync`1"
          "System.Threading.Tasks.Task"
          "System.Threading.Tasks.Task`1"
          "System.Threading.Tasks.ValueTask"
          "System.Threading.Tasks.ValueTask`1" ]

let private isUnit fsharpType = definitionName fsharpType = "Microsoft.FSharp.Core.Unit"

/// Signals from the members and fields a declaration defines itself. Locals inside function bodies are not members.
let private memberSignals (entity: FSharpEntity) =
    [ for value in entity.MembersFunctionsAndValues do
          if not value.IsCompilerGenerated && not value.IsConstructor then
              let name = value.DisplayName

              if value.IsPropertySetterMethod
                 || value.IsMutable
                 || definitionName value.FullType = "Microsoft.FSharp.Core.FSharpRef`1" then
                  MutableState name
              else
                  let returned = value.ReturnParameter.Type

                  if isUnit returned then
                      ReturnsUnit name

                  if asyncTypes.Contains(definitionName returned) then
                      ReturnsAsync name

                  // `let now () = ...` in a module. A parameterless method is not flagged.
                  if not value.IsMember
                     && value.CurriedParameterGroups |> Seq.exists (fun group -> group.Count = 1 && isUnit group[0].Type) then
                      TakesUnit name

      for field in entity.FSharpFields do
          if field.IsMutable then
              MutableState field.Name ]

/// The effectful framework API a symbol use refers to, if any.
let private effectfulApiOf (symbol: FSharpSymbol) =
    try
        match symbol with
        | :? FSharpMemberOrFunctionOrValue as value ->
            value.DeclaringEntity
            |> Option.bind (fun entity -> EffectfulApis.classify entity.AccessPath entity.DisplayName (Some value.DisplayName))
        | :? FSharpEntity as entity when not entity.IsNamespace ->
            EffectfulApis.classify entity.AccessPath entity.DisplayName None
        | _ -> None
    with _ ->
        None

let private declare project (entity: FSharpEntity) (apiSignals: EffectSignal list) : Declaration =
    let functional, classic, isData = Classifier.classify entity

    // Interfaces and abstract classes are ports: their effects belong to whoever implements them.
    let signals =
        if functional = Abstract && classic = Abstract then
            []
        else
            memberSignals entity @ apiSignals |> List.distinct

    { Key = keyOf entity
      Project = project
      NamespacePath = namespaceOf entity
      ModulePath = modulePathOf entity
      FunctionalAbstraction = functional
      ClassicAbstraction = classic
      IsData = isData
      EffectSignals = signals }

/// The .NET reference packs and FSharp.Core, which never count as dependencies.
let private isFramework (entity: FSharpEntity) =
    let inReferencePack =
        entity.Assembly.FileName
        |> Option.exists (fun file -> file.Split([| '\\'; '/' |]) |> Array.contains "packs")

    entity.Assembly.SimpleName = "FSharp.Core" || inReferencePack

let private extractProject (checker: FSharpChecker) (project: FSharpProjectOptions) (results: FSharpCheckProjectResults) =
    let parsingOptions, _ = checker.GetParsingOptionsFromProjectOptions project
    let indexes = Dictionary<string, ScopeIndex.ScopeIndex>(StringComparer.OrdinalIgnoreCase)

    for file in project.SourceFiles |> Array.filter isAnalysedFile do
        let parsed =
            checker.ParseFile(file, SourceText.ofString (File.ReadAllText file), parsingOptions)
            |> Async.RunSynchronously

        indexes[file] <- ScopeIndex.ofParseTree parsed.ParseTree

    let uses =
        results.GetAllUsesOfAllSymbols()
        |> Array.filter (fun symbolUse -> indexes.ContainsKey symbolUse.Range.FileName)

    // Names are not unique: a type and module can share one, and so can generic types of different arity.
    let entitiesByScope = Dictionary<range, FSharpEntity>()
    let entitiesByName = Dictionary<string * ScopeIndex.ScopeKind, FSharpEntity>()

    for symbolUse in uses do
        match symbolUse.Symbol with
        | :? FSharpEntity as entity when symbolUse.IsFromDefinition && not entity.IsNamespace ->
            match ScopeIndex.enclosing indexes[symbolUse.Range.FileName] symbolUse.Range with
            | Some scope when lastSegment scope.Name = entity.DisplayName ->
                entitiesByScope[scope.Range] <- entity
                entitiesByName[(scope.Name, scope.Kind)] <- entity
            | _ -> ()
        | _ -> ()

    // An intrinsic augmentation has no definition of its own, so it is found through its type's name.
    let entityOf (scope: ScopeIndex.Scope) =
        match entitiesByScope.TryGetValue scope.Range with
        | true, entity -> Some entity
        | false, _ ->
            match entitiesByName.TryGetValue((scope.Name, scope.Kind)) with
            | true, entity -> Some entity
            | false, _ -> None

    let projectName = Path.GetFileNameWithoutExtension project.ProjectFileName

    // Effectful API uses are framework uses, so they are found here, before the framework filter.
    let apiSignals = Dictionary<DeclarationKey, ResizeArray<EffectSignal>>()

    let edges =
        [ for symbolUse in uses do
              let index = indexes[symbolUse.Range.FileName]

              if not (ScopeIndex.isInOpen index symbolUse.Range) then
                  match ScopeIndex.enclosing index symbolUse.Range |> Option.bind entityOf with
                  | Some source ->
                      match effectfulApiOf symbolUse.Symbol with
                      | Some api ->
                          let key = keyOf source

                          if not (apiSignals.ContainsKey key) then
                              apiSignals[key] <- ResizeArray()

                          apiSignals[key].Add(UsesEffectfulApi(source.DisplayName, api))
                      | None -> ()

                      for target in targetsOf symbolUse |> List.filter (isFramework >> not) do
                          { From = keyOf source
                            To = keyOf target }
                  | None -> () ]

    let apiSignalsOf entity =
        match apiSignals.TryGetValue(keyOf entity) with
        | true, signals -> List.ofSeq signals
        | false, _ -> []

    entitiesByScope.Values |> Seq.map (fun entity -> declare projectName entity (apiSignalsOf entity)) |> Seq.toList,
    edges

/// Classified declarations and every dependency outside the framework, NuGet packages included.
/// Extraction.merge decides which dependencies count.
let extract (solution: LoadedSolution) (results: FSharpCheckProjectResults list) : Extraction =
    let extracted =
        List.zip solution.Projects results
        |> List.map (fun (project, projectResults) -> extractProject solution.Checker project projectResults)

    { Declarations = extracted |> List.collect fst
      Edges = extracted |> List.collect snd |> List.distinct }

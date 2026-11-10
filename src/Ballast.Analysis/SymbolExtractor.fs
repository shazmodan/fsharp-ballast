module Ballast.Analysis.SymbolExtractor

open System
open System.Collections.Generic
open System.IO
open FSharp.Compiler.CodeAnalysis
open FSharp.Compiler.Symbols
open FSharp.Compiler.Text
open Ballast.Core
open Ballast.Analysis.ProjectLoader

type ExtractOptions = { IncludeExternal: bool }

type LocatedDeclaration =
    { Key: DeclarationKey
      Project: string
      NamespacePath: string
      ModulePath: string option
      Entity: FSharpEntity }

type Extraction =
    { Declarations: LocatedDeclaration list
      Edges: Edge list }

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

let private locate project (entity: FSharpEntity) =
    { Key = keyOf entity
      Project = project
      NamespacePath = namespaceOf entity
      ModulePath = modulePathOf entity
      Entity = entity }

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

    entitiesByScope.Values |> Seq.map (locate projectName) |> Seq.toList,
    [ for symbolUse in uses do
          let index = indexes[symbolUse.Range.FileName]

          if not (ScopeIndex.isInOpen index symbolUse.Range) then
              match ScopeIndex.enclosing index symbolUse.Range |> Option.bind entityOf with
              | Some source ->
                  for target in targetsOf symbolUse |> List.filter (isFramework >> not) do
                      { From = keyOf source
                        To = keyOf target }
              | None -> () ]

let extract
    (options: ExtractOptions)
    (solution: LoadedSolution)
    (results: FSharpCheckProjectResults list)
    : Extraction =
    let extracted =
        List.zip solution.Projects results
        |> List.map (fun (project, projectResults) -> extractProject solution.Checker project projectResults)

    let declarations = extracted |> List.collect fst
    let analysedAssemblies = declarations |> List.map (fun declaration -> declaration.Key.Assembly) |> set

    let isCounted edge =
        edge.From <> edge.To && (options.IncludeExternal || analysedAssemblies.Contains edge.To.Assembly)

    { Declarations = declarations
      Edges = extracted |> List.collect snd |> List.filter isCounted |> List.distinct }

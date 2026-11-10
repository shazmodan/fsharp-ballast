module Ballast.Analysis.ScopeIndex

open FSharp.Compiler.Syntax
open FSharp.Compiler.Text

type ScopeKind =
    | TypeScope
    | ModuleScope

type Scope =
    { Name: string
      Kind: ScopeKind
      Range: range }

type ScopeIndex =
    private
        { Scopes: Scope list
          Opens: range list }

let private dotted (names: LongIdent) =
    names |> List.map (fun name -> name.idText) |> String.concat "."

let private qualified path name =
    if path = "" then dotted name else path + "." + dotted name

let private isAugmentation (SynTypeDefn(typeRepr = representation)) =
    match representation with
    | SynTypeDefnRepr.ObjectModel(kind = SynTypeDefnKind.Augmentation _) -> true
    | _ -> false

let private typeName (SynTypeDefn(typeInfo = SynComponentInfo(longId = name))) = dotted name

let private typesDeclaredIn (declarations: SynModuleDecl list) =
    declarations
    |> List.collect (fun declaration ->
        match declaration with
        | SynModuleDecl.Types(definitions, _) ->
            definitions |> List.filter (isAugmentation >> not) |> List.map typeName
        | _ -> [])

let rec private scopesIn path (declarations: SynModuleDecl list) =
    let declaredHere = typesDeclaredIn declarations

    // An augmentation of a type declared alongside it is intrinsic and belongs to that type.
    // Anything else is an optional extension, which belongs to the enclosing module.
    let hasOwnScope definition =
        not (isAugmentation definition) || List.contains (typeName definition) declaredHere

    declarations
    |> List.collect (fun declaration ->
        match declaration with
        | SynModuleDecl.NestedModule(moduleInfo = SynComponentInfo(longId = name); decls = nested; range = range) ->
            let modulePath = qualified path name
            { Name = modulePath; Kind = ModuleScope; Range = range } :: scopesIn modulePath nested
        | SynModuleDecl.Types(definitions, _) ->
            definitions
            |> List.filter hasOwnScope
            |> List.map (fun (SynTypeDefn(typeInfo = SynComponentInfo(longId = name); range = range)) ->
                { Name = qualified path name
                  Kind = TypeScope
                  Range = range })
        | SynModuleDecl.Exception(
            exnDefn = SynExceptionDefn(
                exnRepr = SynExceptionDefnRepr(caseName = SynUnionCase(ident = SynIdent(name, _)))
            )
            range = range) ->
            [ { Name = qualified path [ name ]
                Kind = TypeScope
                Range = range } ]
        | _ -> [])

let rec private opensIn (declarations: SynModuleDecl list) =
    declarations
    |> List.collect (fun declaration ->
        match declaration with
        | SynModuleDecl.Open(_, range) -> [ range ]
        | SynModuleDecl.NestedModule(decls = nested) -> opensIn nested
        | _ -> [])

let ofParseTree (tree: ParsedInput) : ScopeIndex =
    match tree with
    | ParsedInput.ImplFile(ParsedImplFileInput(contents = contents)) ->
        { Scopes =
            contents
            |> List.collect (fun (SynModuleOrNamespace(longId = name; kind = kind; decls = declarations; range = range)) ->
                let path = if kind.IsGlobalNamespace then "" else dotted name

                if kind.IsModule then
                    { Name = path; Kind = ModuleScope; Range = range } :: scopesIn path declarations
                else
                    scopesIn path declarations)
          Opens = contents |> List.collect (fun (SynModuleOrNamespace(decls = declarations)) -> opensIn declarations) }
    | ParsedInput.SigFile _ -> { Scopes = []; Opens = [] }

let enclosing (index: ScopeIndex) (location: range) : Scope option =
    match index.Scopes |> List.filter (fun scope -> Range.rangeContainsRange scope.Range location) with
    | [] -> None
    | containing -> Some(containing |> List.maxBy (fun scope -> scope.Range.StartLine, scope.Range.StartColumn))

let isInOpen (index: ScopeIndex) (location: range) : bool =
    index.Opens |> List.exists (fun openRange -> Range.rangeContainsRange openRange location)

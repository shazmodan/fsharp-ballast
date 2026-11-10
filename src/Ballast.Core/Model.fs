namespace Ballast.Core

/// Path is FSharpEntity.AccessPath. Name is FSharpEntity.CompiledName, which includes generic arity.
type DeclarationKey =
    { Assembly: string
      Path: string
      Name: string }

type Abstraction =
    | Abstract
    | Concrete
    | NotCounted

/// Evidence that a concrete declaration's own code has side effects, with the member it was found in.
type EffectSignal =
    | ReturnsUnit of memberName: string
    | ReturnsAsync of memberName: string
    | TakesUnit of memberName: string
    | MutableState of memberName: string
    | UsesEffectfulApi of memberName: string * api: string

type Declaration =
    { Key: DeclarationKey
      Project: string
      NamespacePath: string
      ModulePath: string option
      FunctionalAbstraction: Abstraction
      ClassicAbstraction: Abstraction
      /// True for records, unions and enums, whatever their abstraction.
      IsData: bool
      /// Empty for pure code. Signals on a declaration that is abstract under a profile are ignored there.
      EffectSignals: EffectSignal list }

type Edge =
    { From: DeclarationKey
      To: DeclarationKey }

/// The declarations and dependencies found in one language's projects, or merged across languages.
type Extraction =
    { Declarations: Declaration list
      Edges: Edge list }

module Extraction =
    /// Unites extractions. Keeps an edge whose target is declared in any of them, and one whose target
    /// is in an assembly none of them declares, such as a NuGet package, only when includeExternal is set.
    let merge (includeExternal: bool) (extractions: Extraction list) : Extraction =
        let declarations = extractions |> List.collect (fun extraction -> extraction.Declarations)
        let declared = declarations |> List.map (fun declaration -> declaration.Key) |> set
        let analysedAssemblies = declared |> Set.map (fun key -> key.Assembly)

        let isCounted edge =
            edge.From <> edge.To
            && (declared.Contains edge.To
                || (includeExternal && not (analysedAssemblies.Contains edge.To.Assembly)))

        { Declarations = declarations
          Edges = extractions |> List.collect (fun extraction -> extraction.Edges) |> List.filter isCounted |> List.distinct }

type Zone =
    | Pain
    | Uselessness

type ComponentMetrics =
    { Name: string
      Nc: int
      Na: int
      Ca: int
      Ce: int
      A: float option
      I: float option
      D: float option
      /// Effectfulness: the share of counted declarations that are concrete and have an effect signal.
      E: float option
      DataShare: float option
      Zone: Zone option }

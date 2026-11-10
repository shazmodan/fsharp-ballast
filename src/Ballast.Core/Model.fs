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

type Declaration =
    { Key: DeclarationKey
      Project: string
      NamespacePath: string
      ModulePath: string option
      FSharpAbstraction: Abstraction
      ClassicAbstraction: Abstraction
      /// True for records, unions and enums, whatever their abstraction.
      IsData: bool }

type Edge =
    { From: DeclarationKey
      To: DeclarationKey }

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
      DataShare: float option
      Zone: Zone option }

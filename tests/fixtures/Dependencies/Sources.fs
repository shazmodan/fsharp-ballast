namespace Fixture.Sources

open Fixture.Targets
open Fixture.Targets.Patterns
open Fixture.Targets.Extensions

module UsesEntity =
    let run (order: Order) = ignore order

module UsesAbbreviation =
    let run (alias: Alias) = ignore alias

module UsesFunction =
    let run () = Functions.helper 1

module UsesMember =
    let run () = ignore ((Functions.makeService ()).Run())

module UsesUnionCase =
    let run () = ignore (Circle 1.0)

module UsesField =
    let run () = ignore { Id = 1 }

module UsesActivePattern =
    let run (x: int) =
        match x with
        | Even -> 1
        | Odd -> 0

module InferredReturn =
    let run () = Functions.makeOrder ()

module OnlyGeneric =
    let run (x: 'a) = x

module OnlyOpens =
    open Fixture.Targets.Functions

    let run () = 1

module OnlyBcl =
    let run () = System.DateTime.Now.Year + 1

module UsesExtension =
    let run () = "abc".Shout()

module UsesInline =
    let run () = Inlines.callRun (Thing())

module InlineBody =
    let inline run (x: int) = Functions.helper x

module UsesBuilder =
    let run () = Builders.builder { return 1 }

module Outer =
    module InnerSource =
        let run () = Functions.helper 1

module Holder =
    type Held = { Value: int }

type TypeSource(service: IService) =
    member _.Run() = service.Run()

[<Marker>]
type Decorated = { Value: int }

type Recursive = { Next: Recursive option }

type Augmented = { Number: int }

type Augmented with
    member _.Make() = Functions.helper 1

type UsesAlias = Alias

type Paired = { PairedValue: int }

module Paired =
    let make () = Functions.helper 1

type Box<'a> = { Item: 'a }

type Box<'a, 'b> =
    { First: Order
      Second: 'b
      Third: 'a }

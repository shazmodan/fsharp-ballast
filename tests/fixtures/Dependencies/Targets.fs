namespace Fixture.Targets

type MarkerAttribute() =
    inherit System.Attribute()

type Order = { Id: int }

type Shape =
    | Circle of float
    | Square of float

type Alias = int

type IService =
    abstract Run: unit -> int

type Thing() =
    member _.Run() = 1

module Functions =
    let helper (x: int) = x + 1
    let makeOrder () : Order = { Id = 1 }

    let makeService () =
        { new IService with
            member _.Run() = 1 }

module Patterns =
    let (|Even|Odd|) (x: int) = if x % 2 = 0 then Even else Odd

module Extensions =
    type System.String with
        member this.Shout() = this.ToUpper()

module Inlines =
    let inline callRun (x: ^T) = (^T: (member Run: unit -> int) x)

type Builder() =
    member _.Return(x: int) = x

module Builders =
    let builder = Builder()

namespace Fixture.Effects

open System
open System.IO

// ---- Effectful: one declaration per signal kind ----

module Logging =
    let write (text: string) : unit = ignore text

module Jobs =
    let fetch (id: int) = async { return id }

module Tasks =
    let fetch (id: int) = Threading.Tasks.Task.FromResult id

module Factories =
    let ticks () = 42L

module Counters =
    let mutable count = 0

module Cells =
    let cell = ref 0

type MutableRecord = { mutable Value: int }

type Settable() =
    member val Name = "" with get, set

type ClassWithMutableField() =
    let mutable hits = 0
    member _.Hits = hits

module Files =
    let read (path: string) = File.ReadAllText path

module Times =
    let later (days: int) = DateTime.Now.AddDays(float days)

module Ids =
    let next (seed: int) = Guid.NewGuid()

module Terminal =
    let read (prompt: string) = Console.ReadLine()

module Printing =
    let echo (value: int) =
        printfn "%d" value
        value

type Service() =
    member _.Save(value: int) = ignore value

// ---- Pure: must have no signals ----

module Pure =
    let add (a: int) (b: int) = a + b
    let combine (folder: string) = Path.Combine(folder, "file.txt")
    let lines (first: string) (second: string) = first + Environment.NewLine + second

    let sum (values: int list) =
        let mutable total = 0

        for value in values do
            total <- total + value

        total

type Data = { Id: int; Name: string }

type Shape =
    | Circle of float
    | Square of float

type Level =
    | Low = 0
    | High = 1

type Money =
    { Cents: int }

    member money.Double = { Cents = money.Cents * 2 }

module Patterns =
    [<Literal>]
    let Answer = 42

    let (|Even|Odd|) (value: int) = if value % 2 = 0 then Even else Odd

type IPort =
    abstract Save: int -> unit

type Clock = unit -> DateTime

[<AttributeUsage(AttributeTargets.Class)>]
type MarkerAttribute() =
    inherit Attribute()
    member val Name = "" with get, set

namespace Fixture.Classification

open System

type IPort =
    abstract Run: unit -> int

[<AbstractClass>]
type AbstractBase() =
    abstract Run: unit -> int

type WithDefault() =
    abstract Run: unit -> int
    default _.Run() = 1

type Clock = unit -> DateTime
type DeepClock = Clock
type Identifier = int

type FunctionRecord =
    { Load: int -> string
      Save: string -> unit }

type AliasFieldRecord = { Now: Clock }

type MixedRecord =
    { Name: string
      Apply: int -> int }

type PlainRecord = { Id: int }
type RecordAlias = PlainRecord

[<Struct>]
type StructRecord = { X: int }

type Callback = delegate of int -> int

type Decision =
    | Yes
    | No

type Level =
    | Low = 0
    | High = 1

[<Struct>]
type Coordinate =
    val X: int
    new(x) = { X = x }

type Service() =
    member _.Run() = 1

exception Failed of string

[<Measure>]
type metre

module WithValues =
    let value = 1

module OnlyTypes =
    type Nested = { NestedValue: int }

type UserId = int
type User = { UserName: string }

/// A free-monad instruction set: an effect interface written as a union.
type Instruction<'next> =
    | ReadUser of UserId * (User -> 'next)
    | WriteUser of User * 'next

/// The plumbing around it, which stays concrete.
type Program<'a> =
    | Free of Instruction<Program<'a>>
    | Pure of 'a

/// Near misses: a generic union holding data, one with an empty case, and one with no function.
type DataBox<'a> = Box of 'a * int

type Holder<'a> =
    | Holding of 'a
    | Empty

type AllNext<'next> =
    | Step of 'next
    | Skip of int * 'next

module OnlyModules =
    module InnerValues =
        let inner = 1

namespace Fixture.Functional

open System

type Clock = unit -> DateTime

type Repo =
    { Load: int -> string
      Save: string -> unit }

type Order = { Id: int }

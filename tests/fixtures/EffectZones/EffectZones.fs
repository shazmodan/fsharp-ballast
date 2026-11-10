// Stable and concrete: one pure component and one effectful one, both used by App.

namespace Fixture.Pure

type Money = { Cents: int }

module Pricing =
    let add (a: Money) (b: Money) = { Cents = a.Cents + b.Cents }

namespace Fixture.Store

module Db =
    let save (text: string) : unit = ignore text

type Cache() =
    let mutable hits = 0
    member _.Hits = hits

namespace Fixture.App

module Main =
    let run () =
        Fixture.Store.Db.save (string (Fixture.Pure.Pricing.add { Cents = 1 } { Cents = 2 }).Cents)

namespace Fixture.Ports

type IStore =
    abstract Save: string -> unit

type INotifier =
    abstract Notify: string -> unit

namespace Fixture.Domain

type Order = { Id: int }

module Pricing =
    let price (o: Order) = o.Id * 2

namespace Fixture.App

open Fixture.Ports
open Fixture.Domain

type Service(store: IStore) =
    member _.Run() = store.Save(string (Pricing.price { Id = 1 }))

module Main =
    let run (n: INotifier) (s: Service) =
        s.Run()
        n.Notify "done"

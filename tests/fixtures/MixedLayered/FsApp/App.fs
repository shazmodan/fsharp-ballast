namespace Mixed.App

open System
open Mixed.Core

module Billing =
    let total (order: Order) = order.Id * 2

type SystemClock() =
    interface IClock with
        member _.Now() = DateTime.Now

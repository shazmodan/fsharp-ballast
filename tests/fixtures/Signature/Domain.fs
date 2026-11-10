namespace Fixture.Signature

type Order = { Id: int }

module Pricing =
    let private factor = 2
    let price (order: Order) = order.Id * factor

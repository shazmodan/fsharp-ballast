namespace Fixture.Signature

type Order = { Id: int }

module Pricing =
    val price: Order -> int

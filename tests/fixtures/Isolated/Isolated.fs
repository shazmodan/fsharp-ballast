namespace Fixture.Isolated.Other

type Unused = { Value: int }

namespace Fixture.Isolated.Alone

module Lonely =
    open Fixture.Isolated.Other

    let value = 1

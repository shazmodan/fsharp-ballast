module Ballast.Tests.Program

open Expecto

[<EntryPoint>]
let main argv =
    runTestsWithCLIArgs [] argv (testList "Ballast" [ MetricsTests.tests; ProjectLoaderTests.tests; ScopeIndexTests.tests; SymbolExtractorTests.tests; ClassifierTests.tests; CliTests.tests ])

module Ballast.Tests.ClassifierTests

open System.IO
open Expecto
open Ballast.Core
open Ballast.Analysis
open Ballast.Analysis.ProjectLoader
open Ballast.Analysis.SymbolExtractor
open Ballast.Tests.Fixtures

let private classified =
    lazy
        (withFixture "Classification" (fun directory ->
            let solution =
                Expect.wantOk (load defaultLoadOptions (Path.Combine(directory, "Classification.fsproj"))) "load"

            let results = Expect.wantOk (check solution) "check"

            (extract solution results.FSharp).Declarations))

[<Tests>]
let tests =
    testSequencedGroup MsBuildGroup
    <| testList "Classifier" [
        for name, fsharp, classic, isData in
            [ "IPort", Abstract, Abstract, false
              "AbstractBase", Abstract, Abstract, false
              "WithDefault", Concrete, Concrete, false
              "Clock", Abstract, NotCounted, false
              "DeepClock", Abstract, NotCounted, false
              "Identifier", NotCounted, NotCounted, false
              "RecordAlias", NotCounted, NotCounted, false
              "FunctionRecord", Abstract, Concrete, true
              "AliasFieldRecord", Abstract, Concrete, true
              "MixedRecord", Concrete, Concrete, true
              "PlainRecord", Concrete, Concrete, true
              "StructRecord", Concrete, Concrete, true
              "Callback", Abstract, Concrete, false
              "Decision", Concrete, Concrete, true
              "Level", Concrete, Concrete, true
              "Coordinate", Concrete, Concrete, false
              "Service", Concrete, Concrete, false
              "Failed", Concrete, Concrete, false
              "metre", NotCounted, NotCounted, false
              "WithValues", Concrete, Concrete, false
              "OnlyTypes", NotCounted, NotCounted, false
              "OnlyModules", NotCounted, NotCounted, false
              "Nested", Concrete, Concrete, true
              "InnerValues", Concrete, Concrete, false
              "Instruction`1", Abstract, Concrete, true
              "Program`1", Concrete, Concrete, true
              "DataBox`1", Concrete, Concrete, true
              "Holder`1", Concrete, Concrete, true
              "AllNext`1", Concrete, Concrete, true ] do
            test name {
                let declaration = classified.Value |> List.find (fun d -> d.Key.Name = name)

                Expect.equal
                    (declaration.FunctionalAbstraction, declaration.ClassicAbstraction, declaration.IsData)
                    (fsharp, classic, isData)
                    "(functional profile, classic profile, is data)"
            }
    ]

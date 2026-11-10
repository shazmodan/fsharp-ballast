module Ballast.Tests.CSharpExtractorTests

open System.IO
open Expecto
open Ballast.Core
open Ballast.Analysis
open Ballast.Analysis.ProjectLoader
open Ballast.Tests.Fixtures

/// Extracts a fixture and merges it alone, as the pipeline does for a single-language solution.
let private extractFixture name relativePath =
    withFixture name (fun directory ->
        let solution = Expect.wantOk (load defaultLoadOptions (Path.Combine(directory, relativePath))) "load"
        let results = Expect.wantOk (check solution) "check"
        Extraction.merge false [ CSharpExtractor.extract results.CSharp ])

let private dependencies =
    lazy (extractFixture "CSharpDependencies" "CSharpDependencies.csproj")

let private targetsOf source (extraction: Extraction) =
    extraction.Edges
    |> List.filter (fun edge -> edge.From.Name = source)
    |> List.map (fun edge -> edge.To.Name)
    |> List.distinct
    |> List.sort

let private expectDependsOn source target =
    Expect.contains (targetsOf source dependencies.Value) target $"{source} depends on {target}"

[<Tests>]
let tests =
    testSequencedGroup MsBuildGroup
    <| testList "CSharpExtractor" [
        test "calling a static method depends on the class that declares it" {
            expectDependsOn "UsesMember" "Functions"
        }

        testList "each kind of syntax creates a dependency" [
            for scenario, source, target in
                [ "a type in a signature", "UsesTypeInSignature", "Order"
                  "a base class", "UsesBaseType", "Base"
                  "an implemented interface", "UsesInterface", "IService"
                  "a generic type", "UsesGenericArgument", "Box`1"
                  "a generic argument", "UsesGenericArgument", "Order"
                  "typeof", "UsesTypeof", "Order"
                  "nameof", "UsesNameof", "Order"
                  "a cast", "UsesCast", "Order"
                  "a type pattern", "UsesPattern", "Order"
                  "an attribute", "UsesAttribute", "MarkerAttribute"
                  "a member of an object that is never named", "UsesMemberOfReturnedObject", "Order"
                  "an object creation", "UsesObjectCreation", "Service"
                  "an extension method", "UsesExtension", "Extensions"
                  "an enum member", "UsesEnumMember", "Color"
                  "a constant", "UsesConstant", "Functions"
                  "a using static import", "UsesStaticImport", "Functions"
                  "a using alias", "UsesAlias", "Order"
                  "a var local", "UsesVar", "Order"
                  "an inferred lambda parameter", "UsesLambdaParameter", "Order"
                  "deconstruction", "UsesDeconstruction", "Point"
                  "a nested type", "UsesNestedType", "Inner"
                  "a type inside a static class", "UsesTypeInStaticClass", "Held"
                  "a delegate", "UsesDelegate", "Handler"
                  "a static class", "StaticSource", "Functions"
                  "both parts of a partial type", "Split", "Order"
                  "the other part of a partial type", "Split", "Color"
                  "top-level statements", "Program", "Functions" ] do
                test scenario { expectDependsOn source target }
        ]

        testList "these create no dependency" [
            for scenario, source in
                [ "type parameters", "OnlyGeneric`1"
                  "the base class library", "OnlyBcl"
                  "locals and dynamic", "OnlyLocalsAndDynamic"
                  "a documentation comment", "OnlyDocComment"
                  "a type referring to itself", "Recursive"
                  "a generated type", "UsesGenerated" ] do
                test scenario { Expect.isEmpty (targetsOf source dependencies.Value) source }
        ]

        testList "declarations know where they live" [
            for name, path, namespacePath, modulePath in
                [ "Order", "Fixture.Targets", "Fixture.Targets", None
                  "Functions", "Fixture.Targets", "Fixture.Targets", Some "Fixture.Targets.Functions"
                  "Held", "Fixture.Targets.Holder", "Fixture.Targets", Some "Fixture.Targets.Holder"
                  "Inner", "Fixture.Targets.Outer", "Fixture.Targets", None
                  "Box`1", "Fixture.Targets", "Fixture.Targets", None
                  "Program", "global", "<global>", None ] do
                test name {
                    let found = dependencies.Value.Declarations |> List.filter (fun d -> d.Key.Name = name)

                    Expect.hasLength found 1 "declared once"
                    Expect.equal found.Head.Key.Path path "key path"
                    Expect.equal found.Head.Project "CSharpDependencies" "project"
                    Expect.equal found.Head.NamespacePath namespacePath "namespace"
                    Expect.equal found.Head.ModulePath modulePath "module"
                }
        ]

        test "a partial type is declared once" {
            let parts = dependencies.Value.Declarations |> List.filter (fun d -> d.Key.Name = "Split")
            Expect.hasLength parts 1 "one declaration"
        }

        test "a file-local type is declared and has dependencies" {
            let hidden = dependencies.Value.Declarations |> List.filter (fun d -> d.Key.Name.Contains "Hidden")
            Expect.hasLength hidden 1 "declared"
            Expect.equal (targetsOf hidden.Head.Key.Name dependencies.Value) [ "Order" ] "depends on Order"
        }

        testList "classification" [
            let classified = lazy (extractFixture "CSharpClassification" "CSharpClassification.csproj")

            for name, functional, classic, isData in
                [ "IPort", Abstract, Abstract, false
                  "AbstractBase", Abstract, Abstract, false
                  "WithVirtual", Concrete, Concrete, false
                  "Callback", Abstract, Concrete, false
                  "FunctionRecord", Abstract, Concrete, true
                  "CallbackRecord", Abstract, Concrete, true
                  "MixedRecord", Concrete, Concrete, true
                  "PlainRecord", Concrete, Concrete, true
                  "EmptyRecord", Concrete, Concrete, true
                  "BodyRecord", Concrete, Concrete, true
                  "AbstractRecord", Abstract, Abstract, true
                  "StructRecord", Concrete, Concrete, true
                  "Level", Concrete, Concrete, true
                  "WithMembers", Concrete, Concrete, false
                  "OnlyTypes", NotCounted, NotCounted, false
                  "Nested", Concrete, Concrete, false
                  "Service", Concrete, Concrete, false
                  "Coordinate", Concrete, Concrete, false
                  "FailedException", Concrete, Concrete, false
                  "MarkerAttribute", Concrete, Concrete, false
                  "Program", Concrete, Concrete, false ] do
                test name {
                    let declaration = classified.Value.Declarations |> List.find (fun d -> d.Key.Name = name)

                    Expect.equal
                        (declaration.FunctionalAbstraction, declaration.ClassicAbstraction, declaration.IsData)
                        (functional, classic, isData)
                        "(functional profile, classic profile, is data)"
                }
        ]

        testList "implicit calls create dependencies" [
            let implicitCalls = lazy (extractFixture "CSharpImplicit" "CSharpImplicit.csproj")

            for scenario, source, target in
                [ "a user-defined implicit conversion", "UsesImplicitConversion", "Money"
                  "a binary operator", "UsesBinaryOperator", "Money"
                  "a unary operator", "UsesUnaryOperator", "Money"
                  "foreach's GetEnumerator", "UsesForeach", "Bag"
                  "foreach's enumerator", "UsesForeach", "BagEnumerator"
                  "await's awaiter", "UsesAwait", "JobAwaiter"
                  "a collection initializer's Add", "UsesCollectionAdd", "BasketExtensions"
                  "deconstruction's Deconstruct", "UsesDeconstruct", "Pair" ] do
                test scenario {
                    Expect.contains (targetsOf source implicitCalls.Value) target $"{source} depends on {target}"
                }
        ]

        testList "effect signals" [
            let effects = lazy (extractFixture "CSharpEffects" "CSharpEffects.csproj")

            let signalsOf name =
                let found = effects.Value.Declarations |> List.filter (fun d -> d.Key.Name = name)
                Expect.hasLength found 1 $"{name} is declared once"
                found.Head.EffectSignals

            let kindOf signal =
                match signal with
                | ReturnsUnit _ -> "ReturnsUnit"
                | ReturnsAsync _ -> "ReturnsAsync"
                | TakesUnit _ -> "TakesUnit"
                | MutableState _ -> "MutableState"
                | UsesEffectfulApi(_, api) -> $"UsesEffectfulApi {api}"

            for name, expected in
                [ "Logger", "ReturnsUnit"
                  "Fetcher", "ReturnsAsync"
                  "ValueFetcher", "ReturnsAsync"
                  "Counter", "MutableState"
                  "Settable", "MutableState"
                  "StaticState", "MutableState"
                  "MutableRecord", "MutableState"
                  "Files", "UsesEffectfulApi System.IO.File"
                  "Times", "UsesEffectfulApi System.DateTime.Now"
                  "Ids", "UsesEffectfulApi System.Guid.NewGuid"
                  "Terminal", "UsesEffectfulApi System.Console" ] do
                test $"{name}: {expected}" {
                    Expect.contains (signalsOf name |> List.map kindOf) expected "signal"
                }

            test "a signal names the member it came from" {
                Expect.equal (signalsOf "Logger") [ ReturnsUnit "Write" ] "Logger.Write"
            }

            for name in
                [ "PureFunctions"; "Data"; "InitOnly"; "WithReadonly"; "Point"; "Level"; "Handler"; "IPort"; "AbstractBase" ] do
                test $"{name} is pure" { Expect.isEmpty (signalsOf name) "no signals" }
        ]

        test "generated code declares nothing" {
            let names = dependencies.Value.Declarations |> List.map (fun d -> d.Key.Name)
            Expect.isFalse (names |> List.contains "GeneratedThing") "generated file"
        }
    ]

module Ballast.Tests.SymbolExtractorTests

open System.IO
open Expecto
open Ballast.Core
open Ballast.Analysis.ProjectLoader
open Ballast.Analysis.SymbolExtractor
open Ballast.Tests.Fixtures

let private internalOnly = false

/// Extracts a fixture and merges it alone, as the pipeline does for a single-language solution.
let private extractFixture name relativePath includeExternal =
    withFixture name (fun directory ->
        let path = Path.Combine(directory, relativePath)
        let solution = Expect.wantOk (load defaultLoadOptions path) "load"
        let results = Expect.wantOk (check solution) "check"
        Extraction.merge includeExternal [ extract solution results ])

let private dependencies =
    lazy (extractFixture "Dependencies" "Dependencies.fsproj" internalOnly)

let private targetsOf source (extraction: Extraction) =
    extraction.Edges
    |> List.filter (fun edge -> edge.From.Name = source)
    |> List.map (fun edge -> edge.To.Name)
    |> List.sort

let private expectDependsOn source target =
    Expect.contains (targetsOf source dependencies.Value) target $"{source} depends on {target}"

[<Tests>]
let tests =
    testSequencedGroup MsBuildGroup
    <| testList "SymbolExtractor" [
        test "using a function depends on the module that declares it" {
            expectDependsOn "UsesFunction" "Functions"
        }

        testList "each kind of symbol creates a dependency" [
            for scenario, source, target in
                [ "a named type", "UsesEntity", "Order"
                  "a type abbreviation", "UsesAbbreviation", "Alias"
                  "a member", "UsesMember", "IService"
                  "a union case", "UsesUnionCase", "Shape"
                  "a record field", "UsesField", "Order"
                  "an active pattern", "UsesActivePattern", "Patterns"
                  "an inferred return type", "InferredReturn", "Order"
                  "an extension method", "UsesExtension", "Extensions"
                  "an inline function", "UsesInline", "Inlines"
                  "a type passed to an SRTP call", "UsesInline", "Thing"
                  "the body of an inline function", "InlineBody", "Functions"
                  "a computation expression builder", "UsesBuilder", "Builders"
                  "a nested module", "InnerSource", "Functions"
                  "a type's member", "TypeSource", "IService"
                  "an attribute", "Decorated", "MarkerAttribute"
                  "an intrinsic augmentation", "Augmented", "Functions"
                  "an abbreviation's target", "UsesAlias", "Alias"
                  "an implicit file module", "Program", "Functions" ] do
                test scenario { expectDependsOn source target }
        ]

        testList "these create no dependency" [
            for scenario, source in
                [ "generic parameters and locals", "OnlyGeneric"
                  "an unused open", "OnlyOpens"
                  "the base class library and FSharp.Core", "OnlyBcl"
                  "a type referring to itself", "Recursive" ] do
                test scenario { Expect.isEmpty (targetsOf source dependencies.Value) source }
        ]

        testList "declarations know where they live" [
            for name, namespacePath, modulePath in
                [ "Order", "Fixture.Targets", None
                  "Functions", "Fixture.Targets", Some "Fixture.Targets.Functions"
                  "InnerSource", "Fixture.Sources", Some "Fixture.Sources.Outer.InnerSource"
                  "Held", "Fixture.Sources", Some "Fixture.Sources.Holder"
                  "Alias", "Fixture.Targets", None
                  "Program", "<global>", Some "Program" ] do
                test name {
                    let found =
                        dependencies.Value.Declarations |> List.filter (fun declaration -> declaration.Key.Name = name)

                    Expect.hasLength found 1 "declared once"
                    Expect.equal found.Head.Project "Dependencies" "project"
                    Expect.equal found.Head.NamespacePath namespacePath "namespace"
                    Expect.equal found.Head.ModulePath modulePath "module"
                }
        ]

        test "a type and a module with the same name stay separate" {
            let names = dependencies.Value.Declarations |> List.map (fun d -> d.Key.Name)

            Expect.contains names "Paired" "the type"
            Expect.contains names "PairedModule" "the module"
            Expect.equal (targetsOf "PairedModule" dependencies.Value) [ "Functions" ] "the module's dependency"
            Expect.isEmpty (targetsOf "Paired" dependencies.Value) "the type has none"
        }

        test "generic types that differ only in arity stay separate" {
            Expect.isEmpty (targetsOf "Box`1" dependencies.Value) "one type parameter"
            Expect.equal (targetsOf "Box`2" dependencies.Value) [ "Order" ] "two type parameters"
        }

        test "a signature file adds no duplicates or extra dependencies" {
            let extraction = extractFixture "Signature" "Signature.fsproj" internalOnly

            let names = extraction.Declarations |> List.map (fun d -> d.Key.Name) |> List.sort
            Expect.equal names [ "Order"; "Pricing" ] "declarations"
            Expect.equal (targetsOf "Pricing" extraction) [ "Order" ] "dependencies"
            Expect.isEmpty (targetsOf "Order" extraction) "Order depends on nothing"
        }

        test "dependencies cross project references with the right assemblies" {
            let extraction = extractFixture "TwoProjects" "TwoProjects.slnx" internalOnly

            let edge = extraction.Edges |> List.find (fun e -> e.From.Name = "Main" && e.To.Name = "Order")
            Expect.equal edge.From.Assembly "App" "source assembly"
            Expect.equal edge.To.Assembly "Lib" "target assembly"
        }

        testList "external packages" [
            test "are left out by default" {
                let extraction = extractFixture "External" "External.fsproj" internalOnly
                Expect.isEmpty (targetsOf "UsesPackage" extraction) "no external edges"
            }

            test "are included on request, without the framework" {
                let extraction = extractFixture "External" "External.fsproj" true
                let targets = extraction.Edges |> List.map (fun e -> e.To.Assembly) |> List.distinct
                Expect.equal targets [ "Expecto" ] "only the NuGet package"
            }
        ]
    ]

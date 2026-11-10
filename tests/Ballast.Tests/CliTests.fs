module Ballast.Tests.CliTests

open System.IO
open System.Text.Json
open Expecto
open Ballast.Cli
open Ballast.Tests.Fixtures

type private Run =
    { ExitCode: int
      Output: string
      Errors: string }

let private runCli (args: string list) =
    use output = new StringWriter()
    use errors = new StringWriter()
    let exitCode = Cli.run (Array.ofList args) output errors

    { ExitCode = exitCode
      Output = output.ToString()
      Errors = errors.ToString() }

let private runOnFixture name relativePath (args: string list) =
    withFixture name (fun directory -> runCli (Path.Combine(directory, relativePath) :: args))

let private componentsOf (json: string) =
    use document = JsonDocument.Parse json

    document.RootElement.GetProperty("components").EnumerateArray()
    |> Seq.map (fun element -> element.GetProperty("name").GetString(), element.Clone())
    |> Map.ofSeq

let private number (element: JsonElement) (property: string) =
    match element.GetProperty(property) with
    | value when value.ValueKind = JsonValueKind.Null -> None
    | value -> Some(value.GetDouble())

let private text (element: JsonElement) (property: string) =
    match element.GetProperty(property) with
    | value when value.ValueKind = JsonValueKind.Null -> None
    | value -> Some(value.GetString())

let private expectRow (components: Map<string, JsonElement>) name (nc, na, ca, ce, a, i, d, zone) =
    let row = Expect.wantSome (components.TryFind name) $"a row for {name}"
    let count property = row.GetProperty(property: string).GetInt32()

    Expect.equal
        (count "nc", count "na", count "ca", count "ce", number row "a", number row "i", number row "d", text row "zone")
        (nc, na, ca, ce, a, i, d, zone)
        $"{name}: (nc, na, ca, ce, a, i, d, zone)"

[<Tests>]
let tests =
    testSequencedGroup MsBuildGroup
    <| testList "Cli" [
        test "the Layered example reports its hand-calculated table as JSON" {
            let run = runOnFixture "Layered" "Layered.fsproj" [ "--format"; "json"; "--profile"; "classic" ]

            Expect.equal run.ExitCode 0 $"exit code, errors: {run.Errors}"
            let components = componentsOf run.Output
            expectRow components "Layered::Fixture.Ports" (2, 2, 2, 0, Some 1.0, Some 0.0, Some 0.0, None)
            expectRow components "Layered::Fixture.Domain" (2, 0, 1, 0, Some 0.0, Some 0.0, Some 1.0, Some "pain")
            expectRow components "Layered::Fixture.App" (2, 0, 0, 2, Some 0.0, Some 1.0, Some 0.0, None)
        }

        testList "exit code 2" [
            test "when the project cannot be loaded" {
                let run = runCli [ Path.Combine(Path.GetTempPath(), "ballast-tests", "missing", "Missing.fsproj") ]

                Expect.equal run.ExitCode 2 "exit code"
                Expect.stringContains run.Errors "Missing.fsproj" "names the file"
                Expect.stringContains run.Errors "File not found" "says why"
            }

            test "when a project has type errors" {
                let run = runOnFixture "TypeError" "TypeError.fsproj" []

                Expect.equal run.ExitCode 2 "exit code"
                Expect.stringContains run.Errors "TypeError" "names the project"
                Expect.stringContains run.Errors "FS0001" "lists the error"
            }

            test "when the arguments are invalid" {
                let run = runCli [ "--no-such-flag" ]

                Expect.equal run.ExitCode 2 "exit code"
                Expect.stringContains run.Errors "--no-such-flag" "names the bad argument"
                Expect.stringContains run.Errors "USAGE" "shows usage"
            }
        ]

        test "--profile changes abstractness for the FunctionalPorts example" {
            let rowFor args =
                let run = runOnFixture "FunctionalPorts" "FunctionalPorts.fsproj" ("--format" :: "json" :: args)
                Expect.equal run.ExitCode 0 $"exit code, errors: {run.Errors}"
                componentsOf run.Output

            expectRow (rowFor []) "FunctionalPorts::Fixture.Functional" (3, 2, 0, 0, Some 0.667, None, None, None)

            expectRow
                (rowFor [ "--profile"; "classic" ])
                "FunctionalPorts::Fixture.Functional"
                (2, 0, 0, 0, Some 0.0, None, None, None)
        }

        test "functional is the default profile and the report names it" {
            let run = runOnFixture "FunctionalPorts" "FunctionalPorts.fsproj" [ "--format"; "json" ]

            Expect.equal run.ExitCode 0 $"exit code, errors: {run.Errors}"
            use document = JsonDocument.Parse run.Output
            Expect.equal (document.RootElement.GetProperty("profile").GetString()) "functional" "reported profile"
        }

        test "--profile fsharp is no longer accepted" {
            let run = runCli [ "Some.fsproj"; "--profile"; "fsharp" ]

            Expect.equal run.ExitCode 2 "exit code"
            Expect.stringContains run.Errors "--profile" "names the option"
        }

        test "--level project measures whole projects" {
            let run = runOnFixture "TwoProjects" "TwoProjects.slnx" [ "--format"; "json"; "--level"; "project"; "--profile"; "classic" ]

            Expect.equal run.ExitCode 0 $"exit code, errors: {run.Errors}"
            let components = componentsOf run.Output
            expectRow components "App" (1, 0, 0, 1, Some 0.0, Some 1.0, Some 0.0, None)
            expectRow components "Lib" (1, 0, 1, 0, Some 0.0, Some 0.0, Some 1.0, Some "pain")
        }

        test "--level module splits modules out of their namespace" {
            let run = runOnFixture "Layered" "Layered.fsproj" [ "--format"; "json"; "--level"; "module"; "--profile"; "classic" ]

            Expect.equal run.ExitCode 0 $"exit code, errors: {run.Errors}"
            let components = componentsOf run.Output
            expectRow components "Layered::Fixture.Domain.Pricing" (1, 0, 1, 1, Some 0.0, Some 0.5, Some 0.5, None)
            expectRow components "Layered::Fixture.Domain" (1, 0, 2, 0, Some 0.0, Some 0.0, Some 1.0, Some "pain")
        }

        test "components with no coupling are isolated and left out of the mean distance" {
            let run = runOnFixture "Isolated" "Isolated.fsproj" [ "--format"; "json" ]

            Expect.equal run.ExitCode 0 $"exit code, errors: {run.Errors}"
            let components = componentsOf run.Output
            expectRow components "Isolated::Fixture.Isolated.Other" (1, 0, 0, 0, Some 0.0, None, None, None)
            expectRow components "Isolated::Fixture.Isolated.Alone" (1, 0, 0, 0, Some 0.0, None, None, None)

            use document = JsonDocument.Parse run.Output
            let summary = document.RootElement.GetProperty "summary"
            Expect.equal (summary.GetProperty("isolated").GetInt32()) 2 "isolated"
            Expect.equal (number summary "meanDistance") None "no mean distance"
        }

        test "--distance-threshold decides which components are given a zone" {
            let run = runOnFixture "Layered" "Layered.fsproj" [ "--format"; "json"; "--distance-threshold"; "1" ]

            Expect.equal run.ExitCode 0 $"exit code, errors: {run.Errors}"
            let domain = (componentsOf run.Output)["Layered::Fixture.Domain"]
            Expect.equal (text domain "zone") None "D = 1 does not exceed a threshold of 1"
        }

        testList "--max-distance" [
            test "exits with code 1 and names the components beyond it" {
                let run = runOnFixture "Layered" "Layered.fsproj" [ "--format"; "json"; "--max-distance"; "0.5" ]

                Expect.equal run.ExitCode 1 "exit code"
                Expect.stringContains run.Errors "Layered::Fixture.Domain" "names the component"
                Expect.isNonEmpty (componentsOf run.Output) "still writes the report"
            }

            test "exits with code 0 when every component is within it" {
                let run = runOnFixture "Layered" "Layered.fsproj" [ "--format"; "json"; "--max-distance"; "1" ]

                Expect.equal run.ExitCode 0 $"exit code, errors: {run.Errors}"
            }

            test "rejects a value outside 0 to 1" {
                let run = runCli [ "Some.fsproj"; "--max-distance"; "1.5" ]

                Expect.equal run.ExitCode 2 "exit code"
                Expect.stringContains run.Errors "--max-distance" "names the option"
            }
        ]

        test "--include-external counts NuGet packages as coupling" {
            let rowFor args =
                let run = runOnFixture "External" "External.fsproj" ("--format" :: "json" :: args)
                Expect.equal run.ExitCode 0 $"exit code, errors: {run.Errors}"
                componentsOf run.Output

            expectRow (rowFor []) "External::Fixture.External" (1, 0, 0, 0, Some 0.0, None, None, None)

            expectRow
                (rowFor [ "--include-external" ])
                "External::Fixture.External"
                (1, 0, 0, 1, Some 0.0, Some 1.0, Some 0.0, None)
        }

        test "the default table lists components by distance, highest first, with a summary" {
            let run = runOnFixture "Layered" "Layered.fsproj" [ "--profile"; "classic" ]

            Expect.equal run.ExitCode 0 $"exit code, errors: {run.Errors}"
            let lines = run.Output.Split('\n') |> Array.map (fun line -> line.TrimEnd())
            let lineIndex (text: string) = lines |> Array.findIndex (fun line -> line.Contains text)

            Expect.stringContains lines[0] "Component" "header"
            Expect.isLessThan (lineIndex "Fixture.Domain") (lineIndex "Fixture.App") "Domain (D = 1) first"
            Expect.isLessThan (lineIndex "Fixture.App") (lineIndex "Fixture.Ports") "then the rest by name"

            let domain = lines[lineIndex "Fixture.Domain"]
            Expect.stringContains domain "1.000" "distance to three decimals"
            Expect.stringContains domain "pain" "zone"
            Expect.stringContains run.Output "3 components, mean distance 0.333, 0 isolated" "summary"
        }

        test "CSV has one row per component and leaves undefined values empty" {
            let layered = runOnFixture "Layered" "Layered.fsproj" [ "--format"; "csv"; "--profile"; "classic" ]
            let isolated = runOnFixture "Isolated" "Isolated.fsproj" [ "--format"; "csv" ]
            let linesOf (run: Run) = run.Output.Split('\n') |> Array.map (fun l -> l.TrimEnd '\r') |> Array.filter ((<>) "")

            let layeredLines = linesOf layered
            Expect.equal layeredLines[0] "name,nc,na,ca,ce,a,i,d,e,dataShare,zone" "header"
            Expect.contains layeredLines "Layered::Fixture.Domain,2,0,1,0,0.000,0.000,1.000,0.000,0.500,pain" "a full row"
            Expect.contains (linesOf isolated) "Isolated::Fixture.Isolated.Alone,1,0,0,0,0.000,,,0.000,0.000," "undefined values"
        }

        testList "effectfulness" [
            let effectZones args = runOnFixture "EffectZones" "EffectZones.fsproj" ("--format" :: "json" :: args)

            let expectEffect (components: Map<string, JsonElement>) name effect =
                Expect.equal (number components[name] "e") effect $"{name}: e"

            test "the EffectZones example matches its hand-calculated table" {
                let run = effectZones []

                Expect.equal run.ExitCode 0 $"exit code, errors: {run.Errors}"
                let components = componentsOf run.Output
                expectRow components "EffectZones::Fixture.Pure" (2, 0, 1, 0, Some 0.0, Some 0.0, Some 1.0, None)
                expectRow components "EffectZones::Fixture.Store" (2, 0, 1, 0, Some 0.0, Some 0.0, Some 1.0, Some "pain")
                // Ce counts declarations, and Main is App's only one, though it uses two components.
                expectRow components "EffectZones::Fixture.App" (1, 0, 0, 1, Some 0.0, Some 1.0, Some 0.0, None)
                expectEffect components "EffectZones::Fixture.Pure" (Some 0.0)
                expectEffect components "EffectZones::Fixture.Store" (Some 1.0)
                expectEffect components "EffectZones::Fixture.App" (Some 1.0)
            }

            test "the classic profile ignores effectfulness" {
                let components = componentsOf (effectZones [ "--profile"; "classic" ]).Output
                Expect.equal (text components["EffectZones::Fixture.Pure"] "zone") (Some "pain") "pure is in pain too"
            }

            test "--effect-threshold moves the line" {
                let components = componentsOf (effectZones [ "--effect-threshold"; "1" ]).Output
                Expect.equal (text components["EffectZones::Fixture.Store"] "zone") None "E = 1 is not beyond 1"
            }

            test "--effect-threshold rejects a value outside 0 to 1" {
                let run = runCli [ "Some.fsproj"; "--effect-threshold"; "2" ]
                Expect.equal run.ExitCode 2 "exit code"
                Expect.stringContains run.Errors "--effect-threshold" "names the option"
            }

            test "--fail-on-zone pain exits with code 1 and names the components" {
                let run = effectZones [ "--fail-on-zone"; "pain" ]

                Expect.equal run.ExitCode 1 "exit code"
                Expect.stringContains run.Errors "EffectZones::Fixture.Store" "names the component"
                Expect.isFalse (run.Errors.Contains "Fixture.Pure") "the pure component is not named"
                Expect.isNonEmpty (componentsOf run.Output) "still writes the report"
            }

            test "--fail-on-zone succeeds when no component is in that zone" {
                let run = effectZones [ "--fail-on-zone"; "uselessness" ]
                Expect.equal run.ExitCode 0 $"exit code, errors: {run.Errors}"
            }

            test "the table and HTML report show E" {
                let table = runOnFixture "EffectZones" "EffectZones.fsproj" []
                let html = runOnFixture "EffectZones" "EffectZones.fsproj" [ "--format"; "html" ]

                Expect.stringContains (table.Output.Split('\n')[0]) "  E  " "table header"
                Expect.stringContains html.Output "<th>E</th>" "HTML table header"
                Expect.stringContains html.Output "data-e=" "HTML tooltip"
            }
        ]

        test "HTML is one self-contained page with the plot and the table" {
            let run = runOnFixture "Layered" "Layered.fsproj" [ "--format"; "html" ]

            Expect.equal run.ExitCode 0 $"exit code, errors: {run.Errors}"
            let page = run.Output
            Expect.stringStarts page "<!doctype html>" "a full page"
            Expect.stringContains page "<svg" "an inline plot"
            Expect.stringContains page "Main sequence" "the main sequence line"
            Expect.stringContains page "Zone of Pain" "the zone of pain"
            Expect.stringContains page "Zone of Uselessness" "the zone of uselessness"
            Expect.stringContains page "<td>Layered::Fixture.Domain</td>" "a table row"
            Expect.stringContains page "1.000" "values to three decimals"
            Expect.isFalse (page.Contains "<script src") "no external scripts"
            Expect.isFalse (page.Contains "<link") "no external stylesheets"
        }

        test "HTML escapes component names" {
            let run = runOnFixture "Dependencies" "Dependencies.fsproj" [ "--format"; "html" ]

            Expect.equal run.ExitCode 0 $"exit code, errors: {run.Errors}"
            Expect.stringContains run.Output "Dependencies::&lt;global&gt;" "escaped"
            Expect.isFalse (run.Output.Contains "::<global>") "never raw"
        }

        test "--output writes the report to a file instead" {
            withFixture "Layered" (fun directory ->
                let file = Path.Combine(directory, "report.json")
                let run = runCli [ Path.Combine(directory, "Layered.fsproj"); "--format"; "json"; "--output"; file ]

                Expect.equal run.ExitCode 0 $"exit code, errors: {run.Errors}"
                Expect.isTrue (File.Exists file) "the file exists"
                Expect.isNonEmpty (componentsOf (File.ReadAllText file)) "it holds the report"
                Expect.stringContains run.Output "report.json" "says where it went")
        }

        test "--exclude leaves matching components out of the report" {
            let run = runOnFixture "Layered" "Layered.fsproj" [ "--format"; "json"; "--exclude"; "*.Ports"; "--exclude"; "*App"; "--profile"; "classic" ]

            Expect.equal run.ExitCode 0 $"exit code, errors: {run.Errors}"
            let components = componentsOf run.Output
            Expect.equal (components |> Map.keys |> List.ofSeq) [ "Layered::Fixture.Domain" ] "only Domain remains"
            expectRow components "Layered::Fixture.Domain" (2, 0, 1, 0, Some 0.0, Some 0.0, Some 1.0, Some "pain")
        }

        test "--no-restore fails on a project that has not been restored" {
            let run = runOnFixture "Layered" "Layered.fsproj" [ "--no-restore" ]

            Expect.equal run.ExitCode 2 "exit code"
            Expect.stringContains run.Errors "not restored" "says why"
        }

        test "test projects are left out unless --include-tests is given" {
            let namesFor args =
                let run = runOnFixture "WithTests" "WithTests.sln" ("--format" :: "json" :: "--profile" :: "classic" :: args)
                Expect.equal run.ExitCode 0 $"exit code, errors: {run.Errors}"
                componentsOf run.Output

            let without = namesFor []
            Expect.equal (without |> Map.keys |> List.ofSeq) [ "Lib::Fixture.WithTests.Lib" ] "only the library"
            expectRow without "Lib::Fixture.WithTests.Lib" (1, 0, 0, 0, Some 0.0, None, None, None)

            let withTests = namesFor [ "--include-tests" ]
            expectRow withTests "Lib::Fixture.WithTests.Lib" (1, 0, 1, 0, Some 0.0, Some 0.0, Some 1.0, Some "pain")
        }

        test "--configuration selects the build configuration" {
            let namesFor args =
                let run = runOnFixture "Configurations" "Configurations.fsproj" ("--format" :: "json" :: args)
                Expect.equal run.ExitCode 0 $"exit code, errors: {run.Errors}"
                componentsOf run.Output |> Map.keys |> List.ofSeq

            Expect.equal (namesFor []) [ "Configurations::Fixture.Config.Core" ] "Debug by default"

            Expect.equal
                (namesFor [ "--configuration"; "Release" ])
                [ "Configurations::Fixture.Config.Core"; "Configurations::Fixture.Config.ReleaseOnly" ]
                "Release adds its own file"
        }

        test "--framework selects one target framework of a multi-targeted project" {
            let namesFor args =
                let run = runOnFixture "MultiTarget" "MultiTarget.fsproj" ("--format" :: "json" :: args)
                Expect.equal run.ExitCode 0 $"exit code, errors: {run.Errors}"
                componentsOf run.Output |> Map.keys |> List.ofSeq

            let core = "MultiTarget::Fixture.Multi.Core"
            let net8Only = "MultiTarget::Fixture.Multi.Net8Only"
            Expect.equal (namesFor []) [ core; net8Only ] "the first framework, net8.0, by default"
            Expect.equal (namesFor [ "--framework"; "net10.0" ]) [ core ] "net10.0"
            Expect.equal (namesFor [ "--framework"; "net8.0" ]) [ core; net8Only ] "net8.0"
        }

        test "a C# solution reports its components" {
            let run = runOnFixture "CSharpProjects" "CSharpProjects.slnx" [ "--format"; "json"; "--profile"; "classic" ]

            Expect.equal run.ExitCode 0 $"exit code, errors: {run.Errors}"
            let components = componentsOf run.Output
            expectRow components "App::Fixture.CSharp.App" (1, 0, 0, 1, Some 0.0, Some 1.0, Some 0.0, None)
            expectRow components "Lib::Fixture.CSharp.Lib" (1, 0, 1, 0, Some 0.0, Some 0.0, Some 1.0, Some "pain")
        }

        test "an F# project using a C# library matches its hand-calculated table" {
            let run = runOnFixture "MixedLayered" "MixedLayered.slnx" [ "--format"; "json" ]

            Expect.equal run.ExitCode 0 $"exit code, errors: {run.Errors}"
            let components = componentsOf run.Output
            expectRow components "CsCore::Mixed.Core" (2, 1, 2, 0, Some 0.5, Some 0.0, Some 0.5, None)
            expectRow components "FsApp::Mixed.App" (2, 0, 0, 2, Some 0.0, Some 1.0, Some 0.0, None)
        }

        test "--help prints usage and succeeds" {
            let run = runCli [ "--help" ]

            Expect.equal run.ExitCode 0 "exit code"
            Expect.stringContains run.Output "USAGE: fsharp-ballast" "usage"
        }
    ]

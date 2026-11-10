module Ballast.Tests.ProjectLoaderTests

open System.IO
open Expecto
open Ballast.Analysis.ProjectLoader
open Ballast.Tests.Fixtures

let private restoring = { Restore = true }

let private projectNames (solution: LoadedSolution) =
    solution.Projects
    |> List.map (fun p -> Path.GetFileNameWithoutExtension p.ProjectFileName)
    |> List.sort

let private expectLoadFailure (result: Result<LoadedSolution, AnalysisError>) (fragment: string) =
    match result with
    | Error(ProjectLoadFailed(_, message)) -> Expect.stringContains message fragment "failure message"
    | Error other -> failtest $"expected a load failure, got {other}"
    | Ok solution -> failtest $"expected a load failure, loaded {projectNames solution}"

[<Tests>]
let tests =
    testSequencedGroup MsBuildGroup
    <| testList "ProjectLoader" [
        test "loads a single project file" {
            withFixture "TwoProjects" (fun directory ->
                let result = load restoring (Path.Combine(directory, "Lib", "Lib.fsproj"))
                let solution = Expect.wantOk result "load"

                Expect.equal (projectNames solution) [ "Lib" ] "projects")
        }

        testList "solution files load every project with references resolved" [
            for file in [ "TwoProjects.sln"; "TwoProjects.slnx" ] do
                test file {
                    withFixture "TwoProjects" (fun directory ->
                        let solution = Expect.wantOk (load restoring (Path.Combine(directory, file))) "load"

                        Expect.equal (projectNames solution) [ "App"; "Lib" ] "projects"

                        let app = solution.Projects |> List.find (fun p -> p.ProjectFileName.EndsWith "App.fsproj")
                        let references = app.ReferencedProjects |> Array.map (fun r -> Path.GetFileName r.OutputFile)

                        Expect.equal references [| "Lib.dll" |] "App references Lib")
                }
        ]

        test "a path that does not exist is a load failure" {
            let path = Path.Combine(Path.GetTempPath(), "ballast-tests", "does-not-exist", "Missing.fsproj")

            expectLoadFailure (load restoring path) "not found"
        }

        test "a malformed project file is a load failure that explains why" {
            withFixture "Malformed" (fun directory ->
                let result = load restoring (Path.Combine(directory, "Malformed.fsproj"))

                expectLoadFailure result "MSB4025")
        }

        test "an unrestored project is a load failure when restore is turned off" {
            withFixture "TwoProjects" (fun directory ->
                let result = load { Restore = false } (Path.Combine(directory, "Lib", "Lib.fsproj"))

                expectLoadFailure result "not restored")
        }

        test "a solution type-checks across its project reference without being built" {
            withFixture "TwoProjects" (fun directory ->
                let solution = Expect.wantOk (load restoring (Path.Combine(directory, "TwoProjects.slnx"))) "load"

                let results = Expect.wantOk (check solution) "check"

                Expect.hasLength results 2 "one result per project"
                let builtFiles = Directory.GetFiles(Path.Combine(directory, "Lib", "bin"), "*", SearchOption.AllDirectories)
                Expect.isEmpty builtFiles "nothing was built")
        }

        test "a project with a type error fails the check and says where" {
            withFixture "TypeError" (fun directory ->
                let solution = Expect.wantOk (load restoring (Path.Combine(directory, "TypeError.fsproj"))) "load"

                match check solution with
                | Error(CompilationFailed(project, errors)) ->
                    Expect.equal project "TypeError" "project"
                    Expect.hasLength errors 1 "errors"
                    Expect.stringContains errors.Head "Bad.fs(4," "location"
                    Expect.stringContains errors.Head "FS0001" "error number"
                | other -> failtest $"expected a compilation failure, got {other}")
        }

        test "C# projects in a mixed solution are left out" {
            withFixture "Mixed" (fun directory ->
                let solution = Expect.wantOk (load restoring (Path.Combine(directory, "Mixed.sln"))) "load"

                Expect.equal (projectNames solution) [ "FSharpPart" ] "projects")
        }

        test "a solution with no F# projects is a load failure" {
            withFixture "CSharpOnly" (fun directory ->
                expectLoadFailure (load restoring (Path.Combine(directory, "CSharpOnly.sln"))) "No F# projects")
        }

        test "a file that is not a project or solution is a load failure" {
            withFixture "TypeError" (fun directory ->
                let path = Path.Combine(directory, "Bad.fs")

                expectLoadFailure (load { Restore = false } path) "Expected a .fsproj, .sln or .slnx file")
        }
    ]

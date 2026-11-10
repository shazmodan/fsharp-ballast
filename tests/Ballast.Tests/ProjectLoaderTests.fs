module Ballast.Tests.ProjectLoaderTests

open System.IO
open Expecto
open Ballast.Analysis.ProjectLoader
open Ballast.Tests.Fixtures

let private restoring = defaultLoadOptions
let private notRestoring = { defaultLoadOptions with Restore = false }

let private projectNames (solution: LoadedSolution) =
    solution.Projects
    |> List.map (fun p -> Path.GetFileNameWithoutExtension p.ProjectFileName)
    |> List.sort

let private csharpNames (solution: LoadedSolution) =
    solution.CSharpProjects |> List.map Path.GetFileNameWithoutExtension |> List.sort

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
                let result = load notRestoring (Path.Combine(directory, "Lib", "Lib.fsproj"))

                expectLoadFailure result "not restored")
        }

        test "a solution type-checks across its project reference without being built" {
            withFixture "TwoProjects" (fun directory ->
                let solution = Expect.wantOk (load restoring (Path.Combine(directory, "TwoProjects.slnx"))) "load"

                let results = Expect.wantOk (check solution) "check"

                Expect.hasLength results.FSharp 2 "one result per project"
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

        test "a mixed solution loads both its F# and its C# projects" {
            withFixture "Mixed" (fun directory ->
                let solution = Expect.wantOk (load restoring (Path.Combine(directory, "Mixed.sln"))) "load"

                Expect.equal (projectNames solution) [ "FSharpPart" ] "F# projects"
                Expect.equal (csharpNames solution) [ "CSharpPart" ] "C# projects")
        }

        test "a C#-only solution loads its C# projects" {
            withFixture "CSharpOnly" (fun directory ->
                let solution = Expect.wantOk (load restoring (Path.Combine(directory, "CSharpOnly.sln"))) "load"

                Expect.isEmpty solution.Projects "no F# projects"
                Expect.equal (csharpNames solution) [ "CSharpPart" ] "C# projects")
        }

        test "a C# project file loads on its own" {
            withFixture "CSharpProjects" (fun directory ->
                let solution = Expect.wantOk (load restoring (Path.Combine(directory, "Lib", "Lib.csproj"))) "load"

                Expect.equal (csharpNames solution) [ "Lib" ] "C# projects")
        }

        test "a C#-only .slnx loads every C# project" {
            withFixture "CSharpProjects" (fun directory ->
                let solution = Expect.wantOk (load restoring (Path.Combine(directory, "CSharpProjects.slnx"))) "load"

                Expect.equal (csharpNames solution) [ "App"; "Lib" ] "C# projects")
        }

        test "a solution with no F# or C# projects is a load failure" {
            withFixture "EmptySolution" (fun directory ->
                expectLoadFailure (load restoring (Path.Combine(directory, "EmptySolution.slnx"))) "No F# or C# projects")
        }

        test "a malformed C# project is a load failure that explains why" {
            withFixture "CSharpMalformed" (fun directory ->
                expectLoadFailure (load restoring (Path.Combine(directory, "CSharpMalformed.csproj"))) "MSB4025")
        }

        test "an unrestored C# project is a load failure when restore is turned off" {
            withFixture "CSharpProjects" (fun directory ->
                expectLoadFailure (load notRestoring (Path.Combine(directory, "Lib", "Lib.csproj"))) "not restored")
        }

        test "C# projects compile with Roslyn across their project reference without being built" {
            withFixture "CSharpProjects" (fun directory ->
                let solution = Expect.wantOk (load restoring (Path.Combine(directory, "CSharpProjects.slnx"))) "load"
                let results = Expect.wantOk (check solution) "check"

                Expect.isEmpty results.FSharp "no F# results"
                Expect.equal (results.CSharp |> List.map (fun c -> c.AssemblyName) |> List.sort) [ "App"; "Lib" ] "compilations"
                let builtFiles = Directory.GetFiles(Path.Combine(directory, "Lib", "bin"), "*.dll", SearchOption.AllDirectories)
                Expect.isEmpty builtFiles "nothing was built")
        }

        test "a mixed solution checks both languages" {
            withFixture "Mixed" (fun directory ->
                let solution = Expect.wantOk (load restoring (Path.Combine(directory, "Mixed.sln"))) "load"
                let results = Expect.wantOk (check solution) "check"

                Expect.hasLength results.FSharp 1 "F# results"
                Expect.equal (results.CSharp |> List.map (fun c -> c.AssemblyName)) [ "CSharpPart" ] "C# compilations")
        }

        test "a C# type error fails the check and says where" {
            withFixture "CSharpTypeError" (fun directory ->
                let solution = Expect.wantOk (load restoring (Path.Combine(directory, "CSharpTypeError.csproj"))) "load"

                match check solution with
                | Error(CompilationFailed(project, errors)) ->
                    Expect.equal project "CSharpTypeError" "project"
                    Expect.hasLength errors 1 "errors"
                    Expect.stringContains errors.Head "Bad.cs(3," "location"
                    Expect.stringContains errors.Head "CS0029" "error id"
                | other -> failtest $"expected a compilation failure, got {other}")
        }

        test "a C# project that references an F# project is a load failure that names both" {
            withFixture "CSharpUsesFSharp" (fun directory ->
                match load restoring (Path.Combine(directory, "CSharpUsesFSharp.slnx")) with
                | Error(ProjectLoadFailed(path, message)) ->
                    Expect.stringContains path "CsApp.csproj" "the C# project"
                    Expect.stringContains message "'CsApp'" "names the C# project"
                    Expect.stringContains message "'FsLib'" "names the F# project"
                    Expect.stringContains message "not supported" "says why"
                | other -> failtest $"expected a load failure, got {other}")
        }

        test "a file that is not a project or solution is a load failure" {
            withFixture "TypeError" (fun directory ->
                let path = Path.Combine(directory, "Bad.fs")

                expectLoadFailure (load notRestoring path) "Expected a .fsproj, .csproj, .sln or .slnx file")
        }
    ]

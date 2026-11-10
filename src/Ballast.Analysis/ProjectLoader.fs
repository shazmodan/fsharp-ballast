module Ballast.Analysis.ProjectLoader

open System
open System.Diagnostics
open System.IO
open Microsoft.CodeAnalysis
open Microsoft.CodeAnalysis.MSBuild
open FSharp.Compiler.CodeAnalysis
open FSharp.Compiler.Diagnostics
open Ionide.ProjInfo
open Ionide.ProjInfo.Types

type LoadOptions =
    { Restore: bool
      IncludeTests: bool
      Configuration: string option
      Framework: string option }

let defaultLoadOptions =
    { Restore = true
      IncludeTests = false
      Configuration = None
      Framework = None }

type AnalysisError =
    | ProjectLoadFailed of path: string * message: string
    | CompilationFailed of project: string * errors: string list

type LoadedSolution =
    { Projects: FSharpProjectOptions list
      CSharpProjects: string list
      /// MSBuild global properties from the options, such as Configuration, for loading C# projects the same way.
      Properties: (string * string) list
      Checker: FSharpChecker }

let private restore (path: string) =
    let info = ProcessStartInfo("dotnet", RedirectStandardOutput = true, RedirectStandardError = true)
    info.ArgumentList.Add "restore"
    info.ArgumentList.Add path
    use dotnet = Process.Start info
    let output = dotnet.StandardOutput.ReadToEndAsync()
    let errors = dotnet.StandardError.ReadToEndAsync()
    dotnet.WaitForExit()
    dotnet.ExitCode, output.Result + errors.Result

let load (options: LoadOptions) (path: string) : Result<LoadedSolution, AnalysisError> =
    let fullPath = Path.GetFullPath path

    let isProjectFile (file: string) = file.EndsWith ".fsproj" || file.EndsWith ".csproj"

    let isSupported =
        [ ".fsproj"; ".csproj"; ".sln"; ".slnx" ] |> List.contains (Path.GetExtension(fullPath).ToLowerInvariant())

    let exitCode, restoreOutput =
        if File.Exists fullPath && isSupported && options.Restore then
            restore fullPath
        else
            0, ""

    if not (File.Exists fullPath) then
        Error(ProjectLoadFailed(fullPath, "File not found."))
    elif not isSupported then
        Error(ProjectLoadFailed(fullPath, "Expected a .fsproj, .csproj, .sln or .slnx file."))
    elif exitCode <> 0 then
        Error(ProjectLoadFailed(fullPath, $"dotnet restore failed:\n{restoreOutput.Trim()}"))
    else
        let tools = Init.init (DirectoryInfo(Path.GetDirectoryName fullPath)) None
        let globalProperties =
            [ yield! options.Configuration |> Option.map (fun value -> "Configuration", value) |> Option.toList
              yield! options.Framework |> Option.map (fun value -> "TargetFramework", value) |> Option.toList ]

        let loader = WorkspaceLoader.Create(tools, globalProperties)

        let loaded =
            if isProjectFile fullPath then
                loader.LoadProjects [ fullPath ]
            else
                loader.LoadSln fullPath
            |> Seq.filter (fun p -> isProjectFile p.ProjectFileName)
            |> Seq.filter (fun p -> options.IncludeTests || not p.ProjectSdkInfo.IsTestProject)
            |> Seq.toList

        // Ionide evaluates an unrestored project without reporting a failure, and RestoreSuccess stays true.
        // Roslyn does not report it either, so this check covers C# projects too.
        match loaded |> List.tryFind (fun (p: ProjectOptions) -> not (File.Exists p.ProjectSdkInfo.ProjectAssetsFile)) with
        | Some unrestored ->
            Error(ProjectLoadFailed(unrestored.ProjectFileName, "Project is not restored. Run 'dotnet restore' first."))
        | None when loaded.IsEmpty -> Error(ProjectLoadFailed(fullPath, "No F# or C# projects found."))
        | None ->
            let fsharp = loaded |> List.filter (fun p -> p.ProjectFileName.EndsWith ".fsproj")
            let projects = FCS.mapManyOptions fsharp |> Seq.toList

            Ok
                { Projects = projects
                  CSharpProjects =
                    loaded |> List.filter (fun p -> p.ProjectFileName.EndsWith ".csproj") |> List.map (fun p -> p.ProjectFileName)
                  Properties = globalProperties
                  Checker = FSharpChecker.Create(projectCacheSize = max 1 projects.Length) }

type CheckedSolution =
    { FSharp: FSharpCheckProjectResults list
      CSharp: Compilation list }

let private checkCSharp (solution: LoadedSolution) : Result<Compilation list, AnalysisError> =
    if solution.CSharpProjects.IsEmpty then
        Ok []
    else
        // Disposing the workspace stops its build host; the compilations stay usable.
        use workspace = MSBuildWorkspace.Create(dict solution.Properties)

        let describe (diagnostic: Diagnostic) =
            let span = diagnostic.Location.GetLineSpan()
            let position = span.StartLinePosition
            $"{Path.GetFileName span.Path}({position.Line + 1},{position.Character}): {diagnostic.Id}: {diagnostic.GetMessage()}"

        // Opening a project also opens the projects it references, so reuse those.
        let openProject (path: string) =
            match
                workspace.CurrentSolution.Projects
                |> Seq.tryFind (fun p -> String.Equals(p.FilePath, path, StringComparison.OrdinalIgnoreCase))
            with
            | Some project -> project
            | None -> workspace.OpenProjectAsync(path).Result

        let rec compileAll compilations remaining =
            match remaining with
            | [] -> Ok(List.rev compilations)
            | (path: string) :: rest ->
                match (try Ok(openProject path) with e -> Error(e.GetBaseException().Message)) with
                | Error message -> Error(ProjectLoadFailed(path, message))
                | Ok project ->
                    let compilation = project.GetCompilationAsync().Result

                    match compilation.GetDiagnostics() |> Seq.filter (fun d -> d.Severity = DiagnosticSeverity.Error) |> Seq.toList with
                    | [] -> compileAll (compilation :: compilations) rest
                    | errors -> Error(CompilationFailed(project.Name, errors |> List.map describe))

        compileAll [] solution.CSharpProjects

let private checkFSharp (solution: LoadedSolution) : Result<FSharpCheckProjectResults list, AnalysisError> =
    let describe (diagnostic: FSharpDiagnostic) =
        let range = diagnostic.Range
        let file = Path.GetFileName range.FileName
        $"{file}({range.StartLine},{range.StartColumn}): {diagnostic.ErrorNumberText}: {diagnostic.Message}"

    let rec checkAll checkedProjects remaining =
        match remaining with
        | [] -> Ok(List.rev checkedProjects)
        | (project: FSharpProjectOptions) :: rest ->
            let results = solution.Checker.ParseAndCheckProject project |> Async.RunSynchronously

            // HasCriticalErrors stays false for ordinary type errors, so read the diagnostics.
            match results.Diagnostics |> Array.filter (fun d -> d.Severity = FSharpDiagnosticSeverity.Error) with
            | [||] -> checkAll (results :: checkedProjects) rest
            | errors ->
                let name = Path.GetFileNameWithoutExtension project.ProjectFileName
                Error(CompilationFailed(name, errors |> Array.map describe |> Array.toList))

    checkAll [] solution.Projects

/// Type-checks the F# projects with FCS and compiles the C# projects with Roslyn, failing on the first errors.
let check (solution: LoadedSolution) : Result<CheckedSolution, AnalysisError> =
    checkFSharp solution
    |> Result.bind (fun fsharp -> checkCSharp solution |> Result.map (fun csharp -> { FSharp = fsharp; CSharp = csharp }))

module Ballast.Analysis.ProjectLoader

open System.Diagnostics
open System.IO
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

    let isSupported =
        [ ".fsproj"; ".sln"; ".slnx" ] |> List.contains (Path.GetExtension(fullPath).ToLowerInvariant())

    let exitCode, restoreOutput =
        if File.Exists fullPath && isSupported && options.Restore then
            restore fullPath
        else
            0, ""

    if not (File.Exists fullPath) then
        Error(ProjectLoadFailed(fullPath, "File not found."))
    elif not isSupported then
        Error(ProjectLoadFailed(fullPath, "Expected a .fsproj, .sln or .slnx file."))
    elif exitCode <> 0 then
        Error(ProjectLoadFailed(fullPath, $"dotnet restore failed:\n{restoreOutput.Trim()}"))
    else
        let tools = Init.init (DirectoryInfo(Path.GetDirectoryName fullPath)) None
        let globalProperties =
            [ yield! options.Configuration |> Option.map (fun value -> "Configuration", value) |> Option.toList
              yield! options.Framework |> Option.map (fun value -> "TargetFramework", value) |> Option.toList ]

        let loader = WorkspaceLoader.Create(tools, globalProperties)

        let loaded =
            if fullPath.EndsWith ".fsproj" then
                loader.LoadProjects [ fullPath ]
            else
                loader.LoadSln fullPath
            |> Seq.filter (fun p -> p.ProjectFileName.EndsWith ".fsproj")
            |> Seq.filter (fun p -> options.IncludeTests || not p.ProjectSdkInfo.IsTestProject)
            |> Seq.toList

        // Ionide evaluates an unrestored project without reporting a failure, and RestoreSuccess stays true.
        match loaded |> List.tryFind (fun (p: ProjectOptions) -> not (File.Exists p.ProjectSdkInfo.ProjectAssetsFile)) with
        | Some unrestored ->
            Error(ProjectLoadFailed(unrestored.ProjectFileName, "Project is not restored. Run 'dotnet restore' first."))
        | None when loaded.IsEmpty -> Error(ProjectLoadFailed(fullPath, "No F# projects found."))
        | None ->
            let projects = FCS.mapManyOptions loaded |> Seq.toList

            Ok
                { Projects = projects
                  Checker = FSharpChecker.Create(projectCacheSize = max 1 projects.Length) }

let check (solution: LoadedSolution) : Result<FSharpCheckProjectResults list, AnalysisError> =
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

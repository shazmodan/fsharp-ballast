module Ballast.Cli.Cli

open System.IO
open System.Text.RegularExpressions
open Argu
open Ballast.Core
open Ballast.Analysis
open Ballast.Analysis.ProjectLoader
open Ballast.Cli.Args

/// Highest distance first, then components with no distance, each group by name.
let private reportOrder (row: ComponentMetrics) =
    (match row.D with
     | Some d -> 0, -d
     | None -> 1, 0.0),
    row.Name

let private describe error =
    match error with
    | ProjectLoadFailed(path, message) -> $"Could not load {path}: {message}"
    | CompilationFailed(project, errors) ->
        $"Project {project} has compile errors:" :: (errors |> List.map (fun e -> "  " + e))
        |> String.concat "\n"

/// A glob over component names: * matches any run of characters and ? any single one.
let private globPattern (glob: string) =
    let pattern = "^" + Regex.Escape(glob).Replace(@"\*", ".*").Replace(@"\?", ".") + "$"
    Regex(pattern, RegexOptions.IgnoreCase)

let private render format report =
    match format with
    | Table -> Reports.table report
    | Json -> Reports.json report
    | Csv -> Reports.csv report
    | Html -> HtmlReport.html report

let private levelOf option =
    match option with
    | LevelOption.Project -> Metrics.Level.Project, "project"
    | LevelOption.Namespace -> Metrics.Level.Namespace, "namespace"
    | LevelOption.Module -> Metrics.Level.Module, "module"

let private profileOf option =
    match option with
    | Functional -> Metrics.Profile.Functional, "functional"
    | Classic -> Metrics.Profile.Classic, "classic"

let private execute (arguments: ParseResults<Arguments>) (stdout: TextWriter) (stderr: TextWriter) =
    let path = arguments.GetResult Path
    let level, levelName = levelOf (arguments.GetResult(Level, defaultValue = LevelOption.Namespace))
    let profile, profileName = profileOf (arguments.GetResult(Profile, defaultValue = Functional))
    let threshold = arguments.GetResult(Distance_Threshold, defaultValue = 0.5)
    let maxDistance = arguments.TryGetResult Max_Distance

    let outOfRange =
        [ "--distance-threshold", Some threshold; "--max-distance", maxDistance ]
        |> List.tryPick (fun (name, value) ->
            match value with
            | Some value when value < 0.0 || value > 1.0 -> Some name
            | _ -> None)

    match outOfRange with
    | Some name ->
        stderr.WriteLine $"ERROR: {name} must be between 0 and 1."
        2
    | None ->
        let options: Pipeline.AnalyseOptions =
            { Load =
                { Restore = not (arguments.Contains No_Restore)
                  IncludeTests = arguments.Contains Include_Tests
                  Configuration = arguments.TryGetResult Configuration
                  Framework = arguments.TryGetResult Framework }
              IncludeExternal = arguments.Contains Include_External
              Metrics =
                { Level = level
                  Profile = profile
                  DistanceThreshold = threshold
                  EffectThreshold = 0.25 } }

        match Pipeline.analyse options path with
        | Error error ->
            stderr.WriteLine(describe error)
            2
        | Ok components ->
            let excluded = arguments.GetResults Exclude |> List.map globPattern

            let shown =
                components
                |> List.filter (fun row -> not (excluded |> List.exists (fun pattern -> pattern.IsMatch row.Name)))

            let report: Reports.Report =
                { Level = levelName
                  Profile = profileName
                  DistanceThreshold = threshold
                  Components = shown |> List.sortBy reportOrder
                  Summary = Metrics.summarize shown }

            let text = render (arguments.GetResult(Format, defaultValue = Table)) report

            match arguments.TryGetResult Output with
            | Some file ->
                File.WriteAllText(file, text)
                stdout.WriteLine $"Report written to {Path.GetFullPath file}"
            | None -> stdout.Write text

            let beyond =
                match maxDistance with
                | Some limit -> report.Components |> List.filter (fun row -> row.D |> Option.exists (fun d -> d > limit))
                | None -> []

            if beyond.IsEmpty then
                0
            else
                stderr.WriteLine $"Components beyond --max-distance {maxDistance.Value}:"

                for row in beyond do
                    stderr.WriteLine $"  {row.Name} (D = {Reports.formatNumber row.D})"

                1

/// Runs the tool and returns the exit code: 0 success, 1 --max-distance exceeded,
/// 2 bad arguments or analysis failed.
let run (argv: string[]) (stdout: TextWriter) (stderr: TextWriter) : int =
    let parser = ArgumentParser.Create<Arguments>(programName = "fsharp-ballast")

    try
        let arguments = parser.Parse argv

        // Argu takes an unknown flag as the main command, so reject it here.
        match arguments.GetResult Path with
        | path when path.StartsWith "-" ->
            stderr.WriteLine $"ERROR: unrecognized argument: '{path}'."
            stderr.Write(parser.PrintUsage())
            2
        | _ -> execute arguments stdout stderr
    with :? ArguParseException as problem ->
        if problem.ErrorCode = ErrorCode.HelpText then
            stdout.Write problem.Message
            0
        else
            stderr.Write problem.Message
            2

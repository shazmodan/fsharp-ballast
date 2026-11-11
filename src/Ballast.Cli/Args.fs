module Ballast.Cli.Args

open Argu

type ReportFormat =
    | Table
    | Json
    | Csv
    | Html

type LevelOption =
    | Project
    | Namespace
    | Module

type ProfileOption =
    | Functional
    | Classic

[<RequireQualifiedAccess>]
type ZoneOption =
    | Pain
    | Uselessness

type Arguments =
    | [<MainCommand; ExactlyOnce>] Path of path: string
    | Level of level: LevelOption
    | Profile of profile: ProfileOption
    | Format of format: ReportFormat
    | Output of file: string
    | Include_Tests
    | Include_External
    | Exclude of glob: string
    | Configuration of name: string
    | Framework of tfm: string
    | No_Restore
    | Distance_Threshold of threshold: float
    | Max_Distance of distance: float
    | Effect_Threshold of threshold: float
    | Fail_On_Zone of zone: ZoneOption
    | No_Recommendations

    interface IArgParserTemplate with
        member this.Usage =
            match this with
            | Path _ -> "the .fsproj, .csproj, .sln or .slnx file to analyse."
            | Level _ -> "what counts as a component: project, namespace or module (default: namespace)."
            | Profile _ -> "what counts as abstract: functional or classic (default: functional)."
            | Format _ -> "report format: table, json, csv or html (default: table)."
            | Output _ -> "write the report to this file instead of the console."
            | Include_Tests -> "include test projects, which are left out by default."
            | Include_External -> "count dependencies on NuGet packages as coupling."
            | Exclude _ -> "leave components whose name matches this glob out of the report. Repeatable."
            | Configuration _ -> "build configuration to analyse (default: Debug)."
            | Framework _ -> "target framework to analyse (default: the project's first)."
            | No_Restore -> "do not run dotnet restore first."
            | Distance_Threshold _ -> "distance beyond which a component is given a zone, 0 to 1 (default: 0.5)."
            | Max_Distance _ -> "exit with code 1 if any component's distance exceeds this, 0 to 1."
            | Effect_Threshold _ ->
                "under the functional profile, effectfulness beyond which a stable concrete component is in the Zone of Pain, 0 to 1 (default: 0.25)."
            | Fail_On_Zone _ -> "exit with code 1 if any component is in this zone: pain or uselessness. Repeatable."
            | No_Recommendations -> "leave the recommendations out of table and html reports."

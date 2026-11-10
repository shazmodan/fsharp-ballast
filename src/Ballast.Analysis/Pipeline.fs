module Ballast.Analysis.Pipeline

open Ballast.Core
open Ballast.Analysis.ProjectLoader

type AnalyseOptions =
    { Load: LoadOptions
      IncludeExternal: bool
      Metrics: Metrics.Options }

/// Loads, type-checks, extracts and measures the projects at a path.
let analyse (options: AnalyseOptions) (path: string) : Result<ComponentMetrics list, AnalysisError> =
    load options.Load path
    |> Result.bind (fun solution ->
        check solution
        |> Result.map (fun results ->
            let merged = Extraction.merge options.IncludeExternal [ SymbolExtractor.extract solution results.FSharp ]
            Metrics.calculate options.Metrics merged.Declarations merged.Edges))

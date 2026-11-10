module Ballast.Analysis.Pipeline

open Ballast.Core
open Ballast.Analysis.ProjectLoader
open Ballast.Analysis.SymbolExtractor

type AnalyseOptions =
    { Load: LoadOptions
      IncludeExternal: bool
      Metrics: Metrics.Options }

/// Loads, type-checks, extracts, classifies and measures the projects at a path.
let analyse (options: AnalyseOptions) (path: string) : Result<ComponentMetrics list, AnalysisError> =
    load options.Load path
    |> Result.bind (fun solution ->
        check solution
        |> Result.map (fun results ->
            let extraction = extract { IncludeExternal = options.IncludeExternal } solution results
            let declarations = extraction.Declarations |> List.map Classifier.classify
            Metrics.calculate options.Metrics declarations extraction.Edges))

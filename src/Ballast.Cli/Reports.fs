module Ballast.Cli.Reports

open System
open System.IO
open System.Text
open System.Text.Encodings.Web
open System.Text.Json
open Ballast.Core

type Report =
    { Level: string
      Profile: string
      DistanceThreshold: float
      Components: ComponentMetrics list
      Summary: Metrics.Summary }

let private rounded (value: float) = Math.Round(value, 3)

/// A metric rounded to three decimals for display, or "-" when it is undefined.
let formatNumber (value: float option) =
    match value with
    | Some value -> (rounded value).ToString("0.000", Globalization.CultureInfo.InvariantCulture)
    | None -> "-"

let zoneName zone =
    match zone with
    | Pain -> "pain"
    | Uselessness -> "uselessness"

let private summaryLine (summary: Metrics.Summary) =
    let noun = if summary.Components = 1 then "component" else "components"
    $"{summary.Components} {noun}, mean distance {formatNumber summary.MeanDistance}, {summary.Isolated} isolated"

/// What the recommendations say before the per-component advice.
let recommendationsIntro =
    "A component is balanced when its abstractness matches its stability: what much depends on should be abstract, and what depends on much should be concrete. To move a component toward the main sequence:"

/// General guidance as (heading, rest of the sentence) pairs.
let recommendationsGuide =
    [ "Below the line",
      ", too concrete for how much depends on it: give dependents abstractions to use, such as interfaces or, in functional code, function types and records of functions. Or move code that changes often out of it, so that less depends on that code."
      "Above the line",
      ", too abstract for how little depends on it: remove abstractions nothing uses, or merge them into the component that uses them. An abstraction is only worth having when others depend on it."
      "In functional code",
      ", keep effects at the edges. A stable component of pure functions and immutable data is safe even when it is concrete, so moving its side effects out is often all it needs." ]

/// What a component far from the main sequence can do to get closer, or None when it is close enough.
let advice (report: Report) (row: ComponentMetrics) =
    let functional = report.Profile = "functional"

    match row.A, row.I, row.D with
    | Some a, Some i, Some d when d > report.DistanceThreshold ->
        match row.Zone with
        | Some Pain when functional ->
            Some
                "Stable, concrete and effectful: much depends on it, and its side effects make it risky to change. Move the side effects to the edge of the program, so this component keeps the pure logic and callers pass effects in as functions or interfaces. Or put an abstraction, such as an interface, a function type or a record of functions, in front of the effectful parts and have dependents use that instead."
        | Some Pain ->
            Some
                "Stable and concrete: much depends on it, so every change ripples out. Extract interfaces or abstract classes that dependents use instead of the concrete types, or split off the parts that change often so that less depends on them."
        | Some Uselessness ->
            Some
                "Abstract and unstable: it declares abstractions, but little depends on them while they depend on much. Remove abstractions that nothing implements or uses, move single-use ones next to their only user, and make the rest depend on less so they become stable."
        | None when functional && a < 0.5 && i < 0.5 ->
            Some
                "Stable and concrete, but mostly pure, so it is not in the Zone of Pain. Pure functions and immutable data rarely need to change, so much can safely depend on them. Keep side effects out of it."
        | _ when a + i < 1.0 ->
            Some
                "More concrete than its stability calls for. Have dependents use abstractions rather than its concrete types, or move code that changes often out of it."
        | _ ->
            Some
                "More abstract than its dependents justify. Remove abstractions that nothing uses, or have it depend on less so that it becomes stable."
    | _ -> None

/// Components with advice, furthest from the main sequence first.
let recommended (report: Report) =
    report.Components
    |> List.choose (fun row -> advice report row |> Option.map (fun text -> row, text))
    |> List.sortByDescending (fun (row, _) -> row.D)

let thresholdText (report: Report) =
    report.DistanceThreshold.ToString("0.##", Globalization.CultureInfo.InvariantCulture)

/// The line above the advice, or the line in its place when no component needs any.
let recommendedHeading (report: Report) (any: bool) =
    if any then
        $"Components further than {thresholdText report} from the main sequence, furthest first."
    else
        $"No component is further than {thresholdText report} from the main sequence, so no changes are suggested."

/// Breaks text into lines of at most width characters, each starting with indent.
let private wrap (indent: string) (width: int) (text: string) =
    let words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries) |> List.ofArray

    let rec go (lines: string list) (current: string) words =
        match words with
        | [] -> List.rev (current :: lines)
        | (word: string) :: rest when current.Length = indent.Length -> go lines (current + word) rest
        | word :: rest when current.Length + 1 + word.Length > width -> go (current :: lines) (indent + word) rest
        | word :: rest -> go lines (current + " " + word) rest

    go [] indent words

/// The recommendations as plain text, the same as the HTML report's section.
let private recommendationsText (report: Report) =
    let width = 100
    let items = recommended report

    [ "Recommendations"
      ""
      yield! wrap "" width recommendationsIntro
      ""
      for heading, rest in recommendationsGuide do
          yield! wrap "  " width ("- " + heading + rest) |> List.mapi (fun i line -> if i = 0 then line else "  " + line)
      ""
      recommendedHeading report (not items.IsEmpty)
      for row, text in items do
          ""
          $"{row.Name}  (A {formatNumber row.A}, I {formatNumber row.I}, D {formatNumber row.D}, E {formatNumber row.E})"
          yield! wrap "  " width text ]

/// The metrics table, followed by the recommendations unless withRecommendations is false.
let table (withRecommendations: bool) (report: Report) : string =
    let header = [ "Component"; "Nc"; "Na"; "Ca"; "Ce"; "A"; "I"; "D"; "E"; "Data"; "Zone" ]

    let rows =
        report.Components
        |> List.map (fun row ->
            [ row.Name
              string row.Nc
              string row.Na
              string row.Ca
              string row.Ce
              formatNumber row.A
              formatNumber row.I
              formatNumber row.D
              formatNumber row.E
              formatNumber row.DataShare
              row.Zone |> Option.map zoneName |> Option.defaultValue "" ])

    let widths =
        header
        |> List.mapi (fun column title -> (title :: (rows |> List.map (List.item column))) |> List.map String.length |> List.max)

    // Names and zones read left to right; numbers line up on the right.
    let format cells =
        cells
        |> List.mapi (fun column (cell: string) ->
            if column = 0 || column = header.Length - 1 then
                cell.PadRight(widths[column])
            else
                cell.PadLeft(widths[column]))
        |> String.concat "  "
        |> fun line -> line.TrimEnd()

    let recommendations =
        if withRecommendations then "" :: recommendationsText report else []

    (format header :: (rows |> List.map format)) @ [ ""; summaryLine report.Summary ] @ recommendations
    |> String.concat "\n"
    |> fun text -> text + "\n"

/// One row per component, with an advice column at the end unless withRecommendations is false.
let csv (withRecommendations: bool) (report: Report) : string =
    let quoted (value: string) =
        if value.IndexOfAny([| ','; '"'; '\n' |]) >= 0 then
            "\"" + value.Replace("\"", "\"\"") + "\""
        else
            value

    let number value =
        match value with
        | Some _ -> formatNumber value
        | None -> ""

    let lines =
        [ "name,nc,na,ca,ce,a,i,d,e,dataShare,zone" + (if withRecommendations then ",advice" else "")
          for row in report.Components do
              [ quoted row.Name
                string row.Nc
                string row.Na
                string row.Ca
                string row.Ce
                number row.A
                number row.I
                number row.D
                number row.E
                number row.DataShare
                row.Zone |> Option.map zoneName |> Option.defaultValue ""
                if withRecommendations then
                    advice report row |> Option.map quoted |> Option.defaultValue "" ]
              |> String.concat "," ]

    String.concat "\n" lines + "\n"

/// The report as JSON, with a recommendations object at the end unless withRecommendations is false.
let json (withRecommendations: bool) (report: Report) : string =
    use stream = new MemoryStream()

    let options =
        JsonWriterOptions(Indented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping)

    use writer = new Utf8JsonWriter(stream, options)

    let optionalNumber (name: string) (value: float option) =
        match value with
        | Some value -> writer.WriteNumber(name, rounded value)
        | None -> writer.WriteNull name

    writer.WriteStartObject()
    writer.WriteString("level", report.Level)
    writer.WriteString("profile", report.Profile)
    writer.WriteStartArray "components"

    for row in report.Components do
        writer.WriteStartObject()
        writer.WriteString("name", row.Name)
        writer.WriteNumber("nc", row.Nc)
        writer.WriteNumber("na", row.Na)
        writer.WriteNumber("ca", row.Ca)
        writer.WriteNumber("ce", row.Ce)
        optionalNumber "a" row.A
        optionalNumber "i" row.I
        optionalNumber "d" row.D
        optionalNumber "e" row.E
        optionalNumber "dataShare" row.DataShare

        match row.Zone with
        | Some zone -> writer.WriteString("zone", zoneName zone)
        | None -> writer.WriteNull "zone"

        writer.WriteEndObject()

    writer.WriteEndArray()
    writer.WriteStartObject "summary"
    writer.WriteNumber("components", report.Summary.Components)
    optionalNumber "meanDistance" report.Summary.MeanDistance
    writer.WriteNumber("isolated", report.Summary.Isolated)
    writer.WriteEndObject()

    if withRecommendations then
        writer.WriteStartObject "recommendations"
        writer.WriteString("introduction", recommendationsIntro)
        writer.WriteStartArray "guide"

        for heading, rest in recommendationsGuide do
            writer.WriteStartObject()
            writer.WriteString("heading", heading)
            writer.WriteString("text", rest.TrimStart(',', ' '))
            writer.WriteEndObject()

        writer.WriteEndArray()
        writer.WriteNumber("distanceThreshold", report.DistanceThreshold)
        writer.WriteStartArray "components"

        for row, text in recommended report do
            writer.WriteStartObject()
            writer.WriteString("name", row.Name)
            writer.WriteString("advice", text)
            writer.WriteEndObject()

        writer.WriteEndArray()
        writer.WriteEndObject()

    writer.WriteEndObject()
    writer.Flush()
    Encoding.UTF8.GetString(stream.ToArray())

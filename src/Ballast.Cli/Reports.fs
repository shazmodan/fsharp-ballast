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

let table (report: Report) : string =
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

    (format header :: (rows |> List.map format)) @ [ ""; summaryLine report.Summary ]
    |> String.concat "\n"
    |> fun text -> text + "\n"

let csv (report: Report) : string =
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
        [ "name,nc,na,ca,ce,a,i,d,e,dataShare,zone"
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
                row.Zone |> Option.map zoneName |> Option.defaultValue "" ]
              |> String.concat "," ]

    String.concat "\n" lines + "\n"

let json (report: Report) : string =
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
    writer.WriteEndObject()
    writer.Flush()
    Encoding.UTF8.GetString(stream.ToArray())

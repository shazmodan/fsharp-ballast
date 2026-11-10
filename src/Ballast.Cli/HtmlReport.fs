module Ballast.Cli.HtmlReport

open System
open System.Globalization
open System.Net
open System.Text
open Ballast.Core
open Ballast.Cli.Reports

// Plot geometry in SVG units: a 400 x 400 square with room for axes.
let private left, top, size = 56.0, 24.0, 400.0
let private width, height = left + size + 24.0, top + size + 48.0
let private x instability = left + instability * size
let private y abstractness = top + (1.0 - abstractness) * size

let private svgNumber (value: float) = value.ToString("0.##", CultureInfo.InvariantCulture)
let private encode (text: string) = WebUtility.HtmlEncode text

let private shortName (name: string) =
    match name.IndexOf "::" with
    | -1 -> name
    | index -> name.Substring(index + 2)

/// The Zone of Pain: past the distance threshold below the main sequence, with A and I both under 0.5.
/// Points are (I, A). The Zone of Uselessness is its mirror image.
let private painZone threshold =
    let reach = 1.0 - threshold

    if reach <= 0.0 then []
    elif reach <= 0.5 then [ 0.0, 0.0; reach, 0.0; 0.0, reach ]
    else [ 0.0, 0.0; 0.5, 0.0; 0.5, 0.5 - threshold; 0.5 - threshold, 0.5; 0.0, 0.5 ]

let private polygon cssClass (points: (float * float) list) =
    if points.IsEmpty then
        ""
    else
        let coordinates =
            points
            |> List.map (fun (i, a) -> $"{svgNumber (x i)},{svgNumber (y a)}")
            |> String.concat " "

        $"""<polygon class="{cssClass}" points="{coordinates}" />"""

let private styles =
    """
:root {
  color-scheme: light;
  --page: #f9f9f7;
  --surface-1: #fcfcfb;
  --text-primary: #0b0b0b;
  --text-secondary: #52514e;
  --text-muted: #898781;
  --grid: #e1e0d9;
  --axis: #c3c2b7;
  --border: rgba(11, 11, 11, 0.10);
  --zone: rgba(11, 11, 11, 0.06);
  --series-1: #2a78d6;
}
@media (prefers-color-scheme: dark) {
  :root:where(:not([data-theme="light"])) {
    color-scheme: dark;
    --page: #0d0d0d;
    --surface-1: #1a1a19;
    --text-primary: #f0efec;
    --text-secondary: #c3c2b7;
    --text-muted: #898781;
    --grid: #2c2c2a;
    --axis: #383835;
    --border: rgba(255, 255, 255, 0.10);
    --zone: rgba(255, 255, 255, 0.07);
    --series-1: #3987e5;
  }
}
:root[data-theme="dark"] {
  color-scheme: dark;
  --page: #0d0d0d;
  --surface-1: #1a1a19;
  --text-primary: #f0efec;
  --text-secondary: #c3c2b7;
  --text-muted: #898781;
  --grid: #2c2c2a;
  --axis: #383835;
  --border: rgba(255, 255, 255, 0.10);
  --zone: rgba(255, 255, 255, 0.07);
  --series-1: #3987e5;
}
* { box-sizing: border-box; }
body {
  margin: 0;
  padding: 24px 16px 48px;
  background: var(--page);
  color: var(--text-primary);
  font: 14px/1.5 system-ui, -apple-system, "Segoe UI", sans-serif;
}
main { max-width: 960px; margin: 0 auto; }
h1 { font-size: 20px; font-weight: 600; margin: 0 0 4px; }
h2 { font-size: 16px; font-weight: 600; margin: 0 0 12px; }
.subtitle, .note { color: var(--text-secondary); margin: 0 0 16px; }
.card {
  background: var(--surface-1);
  border: 1px solid var(--border);
  border-radius: 12px;
  padding: 16px;
  margin-bottom: 16px;
}
.plot { position: relative; max-width: 560px; }
.plot svg { display: block; width: 100%; height: auto; }
.grid { stroke: var(--grid); stroke-width: 1; }
.axis { stroke: var(--axis); stroke-width: 1; }
.tick, .axis-title, .zone-label, .point-label {
  fill: var(--text-muted);
  font-size: 12px;
  font-variant-numeric: tabular-nums;
}
.axis-title { fill: var(--text-secondary); }
@media (max-width: 520px) {
  /* The plot scales down on narrow screens, so its text is set larger in SVG units. */
  .tick, .axis-title, .zone-label, .point-label, .sequence-label { font-size: 18px; }
}
.zone { fill: var(--zone); }
.zone-label { fill: var(--text-secondary); }
.sequence { stroke: var(--text-muted); stroke-width: 2; stroke-linecap: round; }
.sequence-label { fill: var(--text-secondary); font-size: 12px; }
.point-label { fill: var(--text-primary); }
.dot { fill: var(--series-1); stroke: var(--surface-1); stroke-width: 2; }
.hit { fill: transparent; cursor: default; outline: none; }
.point:hover .dot, .point:focus-within .dot { r: 7; }
.hit:focus-visible { stroke: var(--text-primary); stroke-width: 1; }
.tooltip {
  position: absolute;
  pointer-events: none;
  background: var(--surface-1);
  border: 1px solid var(--border);
  border-radius: 8px;
  padding: 8px 10px;
  box-shadow: 0 2px 8px rgba(0, 0, 0, 0.12);
  max-width: 280px;
  font-size: 12px;
}
.tooltip-value { font-weight: 600; font-variant-numeric: tabular-nums; }
.tooltip-name { color: var(--text-secondary); overflow-wrap: anywhere; }
.table-wrap { overflow-x: auto; }
table { border-collapse: collapse; width: 100%; font-variant-numeric: tabular-nums; }
th, td { padding: 6px 10px; border-bottom: 1px solid var(--grid); white-space: nowrap; }
th { color: var(--text-secondary); font-weight: 600; text-align: right; }
td { text-align: right; }
th:first-child, td:first-child, th:last-child, td:last-child { text-align: left; }
"""

let private script =
    """
(() => {
  const plot = document.getElementById('plot');
  const tip = document.getElementById('tooltip');
  function show(event) {
    const target = event.currentTarget;
    tip.replaceChildren();
    const value = document.createElement('div');
    value.className = 'tooltip-value';
    value.textContent = 'A ' + target.dataset.a + ' · I ' + target.dataset.i + ' · D ' + target.dataset.d + ' · E ' + target.dataset.e;
    tip.append(value);
    for (const name of target.dataset.names.split('\n')) {
      const row = document.createElement('div');
      row.className = 'tooltip-name';
      row.textContent = name;
      tip.append(row);
    }
    tip.hidden = false;
    const box = plot.getBoundingClientRect();
    const mark = target.getBoundingClientRect();
    let x = mark.right - box.left + 4;
    if (x + tip.offsetWidth > box.width) x = mark.left - box.left - tip.offsetWidth - 4;
    tip.style.left = Math.max(0, x) + 'px';
    tip.style.top = Math.max(0, mark.top - box.top - 8) + 'px';
  }
  function hide() { tip.hidden = true; }
  for (const hit of document.querySelectorAll('.hit')) {
    hit.addEventListener('pointerenter', show);
    hit.addEventListener('focus', show);
    hit.addEventListener('pointerleave', hide);
    hit.addEventListener('blur', hide);
  }
})();
"""

let private plot (report: Report) =
    let svg = StringBuilder()
    let add (text: string) = svg.Append(text).Append('\n') |> ignore

    add
        $"""<svg viewBox="0 0 {svgNumber width} {svgNumber height}" role="img" aria-labelledby="plot-title">"""

    add """<title id="plot-title">Abstractness against instability for each component</title>"""

    let painPoints = painZone report.DistanceThreshold
    add (polygon "zone" painPoints)
    add (polygon "zone" (painPoints |> List.map (fun (i, a) -> 1.0 - i, 1.0 - a)))

    for tick in [ 0.0; 0.25; 0.5; 0.75; 1.0 ] do
        let label = tick.ToString("0.##", CultureInfo.InvariantCulture)
        let gridClass = if tick = 0.0 then "axis" else "grid"
        add $"""<line class="{gridClass}" x1="{svgNumber (x tick)}" y1="{svgNumber top}" x2="{svgNumber (x tick)}" y2="{svgNumber (top + size)}" />"""
        add $"""<line class="{gridClass}" x1="{svgNumber left}" y1="{svgNumber (y tick)}" x2="{svgNumber (left + size)}" y2="{svgNumber (y tick)}" />"""
        add $"""<text class="tick" x="{svgNumber (x tick)}" y="{svgNumber (top + size + 18.0)}" text-anchor="middle">{label}</text>"""
        add $"""<text class="tick" x="{svgNumber (left - 8.0)}" y="{svgNumber (y tick + 4.0)}" text-anchor="end">{label}</text>"""

    add $"""<text class="axis-title" x="{svgNumber (x 0.5)}" y="{svgNumber (height - 6.0)}" text-anchor="middle">Instability (I)</text>"""

    add
        $"""<text class="axis-title" transform="translate(14 {svgNumber (y 0.5)}) rotate(-90)" text-anchor="middle">Abstractness (A)</text>"""

    // Zone labels sit at the inner top edge of each zone, away from the corners where points gather.
    if not painPoints.IsEmpty then
        let edge = min (1.0 - report.DistanceThreshold) 0.5
        add $"""<text class="zone-label" x="{svgNumber (x 0.0 + 8.0)}" y="{svgNumber (y edge + 16.0)}">Zone of Pain</text>"""

        add
            $"""<text class="zone-label" x="{svgNumber (x 1.0 - 8.0)}" y="{svgNumber (y (1.0 - edge) - 8.0)}" text-anchor="end">Zone of Uselessness</text>"""

    add $"""<line class="sequence" x1="{svgNumber (x 0.0)}" y1="{svgNumber (y 1.0)}" x2="{svgNumber (x 1.0)}" y2="{svgNumber (y 0.0)}" />"""

    add
        $"""<text class="sequence-label" transform="translate({svgNumber (x 0.62 + 7.0)} {svgNumber (y 0.38 - 7.0)}) rotate(45)" text-anchor="middle">Main sequence</text>"""

    // Components at the same point share one mark; the tooltip lists them all.
    let groups =
        report.Components
        |> List.choose (fun row ->
            match row.A, row.I with
            | Some a, Some i -> Some((Math.Round(a, 3), Math.Round(i, 3)), row)
            | _ -> None)
        |> List.groupBy fst
        |> List.map (fun (point, rows) -> point, rows |> List.map snd)

    let labelled =
        groups
        |> List.filter (fun (_, rows) -> rows |> List.exists (fun row -> row.Zone.IsSome))
        |> List.truncate 5
        |> List.map fst
        |> set

    for (a, i), rows in groups do
        let cx, cy = x i, y a
        let names = rows |> List.map (fun row -> row.Name) |> String.concat "\n"
        let first = rows.Head
        let description = $"{rows.Length} component(s) at A {formatNumber (Some a)}, I {formatNumber (Some i)}"

        add "<g class=\"point\">"
        add $"""<circle class="dot" cx="{svgNumber cx}" cy="{svgNumber cy}" r="5" />"""

        add
            $"""<circle class="hit" cx="{svgNumber cx}" cy="{svgNumber cy}" r="12" tabindex="0" aria-label="{encode description}" data-names="{encode names}" data-a="{formatNumber (Some a)}" data-i="{formatNumber (Some i)}" data-d="{formatNumber first.D}" data-e="{formatNumber first.E}" />"""

        if labelled.Contains(a, i) then
            let text =
                shortName first.Name + (if rows.Length > 1 then $" +{rows.Length - 1}" else "")

            let anchor, offset = if i > 0.7 then "end", -10.0 else "start", 10.0
            add $"""<text class="point-label" x="{svgNumber (cx + offset)}" y="{svgNumber (cy + 4.0)}" text-anchor="{anchor}">{encode text}</text>"""

        add "</g>"

    add "</svg>"
    svg.ToString(), report.Components.Length - (groups |> List.sumBy (fun (_, rows) -> rows.Length))

let private metricsTable (report: Report) =
    let body = StringBuilder()

    for row in report.Components do
        let zone =
            match row.Zone with
            | Some Pain -> "pain"
            | Some Uselessness -> "uselessness"
            | None -> ""

        body.Append(
            $"<tr><td>{encode row.Name}</td><td>{row.Nc}</td><td>{row.Na}</td><td>{row.Ca}</td><td>{row.Ce}</td>"
            + $"<td>{formatNumber row.A}</td><td>{formatNumber row.I}</td><td>{formatNumber row.D}</td><td>{formatNumber row.E}</td>"
            + $"<td>{formatNumber row.DataShare}</td><td>{zone}</td></tr>\n"
        )
        |> ignore

    "<div class=\"table-wrap\"><table>\n"
    + "<thead><tr><th>Component</th><th>Nc</th><th>Na</th><th>Ca</th><th>Ce</th><th>A</th><th>I</th><th>D</th><th>E</th><th>Data</th><th>Zone</th></tr></thead>\n"
    + $"<tbody>\n{body}</tbody>\n</table></div>"

let html (report: Report) : string =
    let svg, unplotted = plot report
    let summary = report.Summary
    let noun = if summary.Components = 1 then "component" else "components"

    let unplottedNote =
        if unplotted = 0 then
            ""
        else
            $"""<p class="note">{unplotted} {noun} with undefined abstractness or instability are not plotted. They are listed in the table.</p>"""

    let threshold = report.DistanceThreshold.ToString("0.##", CultureInfo.InvariantCulture)

    $"""<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>Package metrics report</title>
<style>{styles}</style>
</head>
<body>
<main>
<h1>Package metrics</h1>
<p class="subtitle">{summary.Components} {noun} at {report.Level} level, {report.Profile} profile. Mean distance {formatNumber summary.MeanDistance}, {summary.Isolated} isolated. Zones start beyond a distance of {threshold}.</p>
<section class="card">
<h2>Distance from the main sequence</h2>
<div class="plot" id="plot">
{svg}<div class="tooltip" id="tooltip" hidden></div>
</div>
{unplottedNote}
</section>
<section class="card">
<h2>All components</h2>
{metricsTable report}
</section>
</main>
<script>{script}</script>
</body>
</html>
"""

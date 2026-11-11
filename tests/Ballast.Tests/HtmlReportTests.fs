module Ballast.Tests.HtmlReportTests

open Expecto
open Ballast.Core
open Ballast.Cli

let row name a i e zone : ComponentMetrics =
    { Name = name
      Nc = 4
      Na = 0
      Ca = 2
      Ce = 1
      A = Some a
      I = Some i
      D = Some(abs (a + i - 1.0))
      E = Some e
      DataShare = Some 0.0
      Zone = zone }

let report profile (rows: ComponentMetrics list) : Reports.Report =
    { Level = "namespace"
      Profile = profile
      DistanceThreshold = 0.5
      Components = rows
      Summary = Metrics.summarize rows }

/// The recommendations section of the page, so a test does not match text elsewhere.
let recommendations (page: string) =
    let start = page.IndexOf "<section class=\"card\" id=\"recommendations\">"
    Expect.isGreaterThanOrEqual start 0 "the page has a recommendations section"
    let finish = page.IndexOf("</section>", start)
    page.Substring(start, finish - start)

let tests =
    testList
        "HTML recommendations"
        [ test "a component in the Zone of Pain under functional is told to move its effects out" {
              let section =
                  recommendations (HtmlReport.html true (report "functional" [ row "App::Store" 0.0 0.1 0.75 (Some Pain) ]))

              Expect.stringContains section "App::Store" "names the component"
              Expect.stringContains section "Move the side effects" "functional advice"
          }

          test "a component in the Zone of Pain under classic is told to add abstractions" {
              let section =
                  recommendations (HtmlReport.html true (report "classic" [ row "App::Store" 0.0 0.1 0.0 (Some Pain) ]))

              Expect.stringContains section "App::Store" "names the component"
              Expect.stringContains section "interfaces or abstract classes" "classic advice"
              Expect.isFalse (section.Contains "Move the side effects") "no effect advice under classic"
          }

          test "a component in the Zone of Uselessness is told to remove or stabilise its abstractions" {
              let section =
                  recommendations (HtmlReport.html true (report "functional" [ row "App::Ports" 1.0 0.9 0.0 (Some Uselessness) ]))

              Expect.stringContains section "App::Ports" "names the component"
              Expect.stringContains section "Remove abstractions that nothing" "uselessness advice"
          }

          test "a pure, stable, concrete component under functional is said to be fine" {
              let section =
                  recommendations (HtmlReport.html true (report "functional" [ row "App::Pricing" 0.0 0.0 0.0 None ]))

              Expect.stringContains section "App::Pricing" "names the component"
              Expect.stringContains section "Keep side effects out of it" "pure advice"
          }

          test "components close to the main sequence get no advice" {
              let section =
                  recommendations (HtmlReport.html true (report "functional" [ row "App::Balanced" 0.5 0.5 0.0 None ]))

              Expect.isFalse (section.Contains "App::Balanced") "not listed"
              Expect.stringContains section "No component is further than 0.5" "says all are balanced"
          }

          test "advice is listed furthest from the main sequence first and names are escaped" {
              let section =
                  recommendations (
                      HtmlReport.html true (
                          report
                              "classic"
                              [ row "App::Near<T>" 0.1 0.3 0.0 (Some Pain)
                                row "App::Far" 0.0 0.0 0.0 (Some Pain) ]
                      )
                  )

              Expect.isLessThan (section.IndexOf "App::Far") (section.IndexOf "App::Near&lt;T&gt;") "furthest first"
          }

          test "the HTML report leaves the recommendations out when asked" {
              let page = HtmlReport.html false (report "functional" [ row "App::Store" 0.0 0.1 0.75 (Some Pain) ])

              Expect.isFalse (page.Contains "id=\"recommendations\"") "no recommendations section"
              Expect.stringContains page "App::Store" "the table is still there"
          }

          test "the table is followed by the same recommendations as the HTML report" {
              let text =
                  Reports.table
                      true
                      (report
                          "classic"
                          [ row "App::Near" 0.1 0.3 0.0 (Some Pain)
                            row "App::Far" 0.0 0.0 0.0 (Some Pain)
                            row "App::Balanced" 0.5 0.5 0.0 None ])

              let recommendations = text.Substring(text.IndexOf "Recommendations")
              Expect.isLessThan (text.IndexOf "mean distance") (text.IndexOf "Recommendations") "after the summary"
              Expect.stringContains recommendations Reports.recommendationsIntro.[0..40] "the introduction"
              Expect.stringContains recommendations "- Below the line, too concrete" "the guide"
              Expect.stringContains recommendations "App::Far  (A 0.000, I 0.000, D 1.000, E 0.000)" "names and values"
              Expect.isLessThan (recommendations.IndexOf "App::Far") (recommendations.IndexOf "App::Near") "furthest first"
              Expect.stringContains recommendations "  Stable and concrete: much depends on it" "indented advice"
              Expect.isFalse (recommendations.Contains "App::Balanced") "balanced components get no advice"

              let longest = text.Split('\n') |> Array.map String.length |> Array.max
              Expect.isLessThanOrEqual longest 100 "advice is wrapped"
          }

          test "the table says when no component needs a change" {
              let text = Reports.table true (report "functional" [ row "App::Balanced" 0.5 0.5 0.0 None ])

              Expect.stringContains text "No component is further than 0.5 from the main sequence" "says all are balanced"
          }

          test "the table leaves the recommendations out when asked" {
              let text = Reports.table false (report "functional" [ row "App::Store" 0.0 0.1 0.75 (Some Pain) ])

              Expect.isFalse (text.Contains "Recommendations") "no recommendations"
              Expect.stringContains text "1 component, mean distance" "ends with the summary"
          } ]

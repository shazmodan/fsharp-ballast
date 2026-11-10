module Ballast.Tests.MetricsTests

open Expecto
open Ballast.Core
open Ballast.Core.Metrics

let options =
    { Level = Level.Namespace
      Profile = Profile.Functional
      DistanceThreshold = 0.5 }

let declaration abstraction ns name =
    { Key = { Assembly = "App"; Path = ns; Name = name }
      Project = "App"
      NamespacePath = ns
      ModulePath = None
      FunctionalAbstraction = abstraction
      ClassicAbstraction = abstraction
      IsData = false }

let concrete = declaration Concrete
let abstractType = declaration Abstract

let componentNamed ns (results: ComponentMetrics list) =
    results |> List.find (fun c -> c.Name = "App::" + ns)

let (=>) (source: Declaration) (target: Declaration) = { From = source.Key; To = target.Key }

let expectClose (actual: float option) (expected: float) message =
    let value = Expect.wantSome actual message
    Expect.floatClose Accuracy.medium value expected message

/// Declarations and edges giving "App.Subject" exactly these counts. Requires ce <= nc.
let shaped (na, nc) (ca, ce) =
    let members =
        [ for n in 1..nc -> declaration (if n <= na then Abstract else Concrete) "App.Subject" $"M{n}" ]

    let outside = concrete "App.Outside" "Target"
    let callers = [ for n in 1..ca -> concrete "App.Callers" $"C{n}" ]

    let edges =
        [ for m in List.truncate ce members -> m => outside ]
        @ [ for c in callers -> c => members.Head ]

    members @ [ outside ] @ callers, edges

[<Tests>]
let tests =
    testList "Metrics" [
        test "abstractness is the share of counted declarations that are abstract" {
            let declarations = [ abstractType "App.Ports" "IStore"; concrete "App.Ports" "Adapter" ]

            let result = calculate options declarations [] |> componentNamed "App.Ports"

            Expect.equal result.Nc 2 "Nc"
            Expect.equal result.Na 1 "Na"
            Expect.equal result.A (Some 0.5) "A"
        }

        test "instability is efferent coupling over total coupling" {
            let first = concrete "App.Api" "First"
            let second = concrete "App.Api" "Second"
            let target = concrete "App.Domain" "Target"
            let caller = concrete "App.Host" "Caller"

            let result =
                calculate options [ first; second; target; caller ]
                    [ first => target; second => target; caller => first ]
                |> componentNamed "App.Api"

            Expect.equal result.Ca 1 "Ca"
            Expect.equal result.Ce 2 "Ce"
            expectClose result.I 0.6666666667 "I"
        }

        test "dependencies inside a component are not coupling" {
            let first = concrete "App.Api" "First"
            let second = concrete "App.Api" "Second"

            let result =
                calculate options [ first; second ] [ first => second ] |> componentNamed "App.Api"

            Expect.equal result.Ca 0 "Ca"
            Expect.equal result.Ce 0 "Ce"
        }

        test "a declaration counts once however many outside declarations it couples to" {
            let source = concrete "App.Api" "Source"
            let targetA = concrete "App.Domain" "TargetA"
            let targetB = concrete "App.Domain" "TargetB"

            let results =
                calculate options [ source; targetA; targetB ]
                    [ source => targetA; source => targetB; source => targetB ]

            Expect.equal (componentNamed "App.Api" results).Ce 1 "Ce counts the source declaration once"
            Expect.equal (componentNamed "App.Domain" results).Ca 1 "Ca counts the source declaration once"
        }

        test "the Layered worked example matches its hand-calculated table" {
            let store = abstractType "App.Ports" "IStore"
            let notifier = abstractType "App.Ports" "INotifier"
            let order = concrete "App.Domain" "Order"
            let pricing = concrete "App.Domain" "Pricing"
            let service = concrete "App.Main" "Service"
            let main = concrete "App.Main" "Main"

            let results =
                calculate options [ store; notifier; order; pricing; service; main ]
                    [ service => store
                      service => pricing
                      service => order
                      main => notifier
                      main => service
                      pricing => order ]

            let expectRow ns (nc, na, ca, ce, a, i, d) =
                let row = componentNamed ns results
                Expect.equal (row.Nc, row.Na, row.Ca, row.Ce) (nc, na, ca, ce) (ns + " counts")
                expectClose row.A a (ns + " A")
                expectClose row.I i (ns + " I")
                expectClose row.D d (ns + " D")

            expectRow "App.Ports" (2, 2, 2, 0, 1.0, 0.0, 0.0)
            expectRow "App.Domain" (2, 0, 1, 0, 0.0, 0.0, 1.0)
            expectRow "App.Main" (2, 0, 0, 2, 0.0, 1.0, 0.0)
        }

        test "a component with no coupling has undefined instability and distance" {
            let result =
                calculate options [ concrete "App.Alone" "Thing" ] [] |> componentNamed "App.Alone"

            Expect.isSome result.A "A is still defined"
            Expect.isNone result.I "I"
            Expect.isNone result.D "D"
        }

        test "a not-counted declaration stays out of Nc but still couples" {
            let alias = declaration NotCounted "App.Types" "Alias"
            let thing = concrete "App.Types" "Thing"
            let target = concrete "App.Domain" "Target"

            let result =
                calculate options [ alias; thing; target ] [ alias => target ] |> componentNamed "App.Types"

            Expect.equal result.Nc 1 "Nc"
            Expect.equal result.Ce 1 "Ce"
        }

        test "a component with nothing counted has undefined abstractness and distance" {
            let alias = declaration NotCounted "App.Types" "Alias"
            let target = concrete "App.Domain" "Target"

            let result =
                calculate options [ alias; target ] [ alias => target ] |> componentNamed "App.Types"

            Expect.equal result.Nc 0 "Nc"
            Expect.isNone result.A "A"
            Expect.isSome result.I "I is still defined"
            Expect.isNone result.D "D"
        }

        test "the FunctionalPorts worked example differs by profile" {
            let functional = "App.Functional"

            let declarations =
                [ { declaration Abstract functional "Clock" with ClassicAbstraction = NotCounted }
                  { declaration Abstract functional "Repo" with ClassicAbstraction = Concrete }
                  concrete functional "Order" ]

            let fsharp = calculate options declarations [] |> componentNamed functional

            let classic =
                calculate { options with Profile = Profile.Classic } declarations []
                |> componentNamed functional

            Expect.equal (fsharp.Nc, fsharp.Na) (3, 2) "fsharp counts"
            expectClose fsharp.A 0.6666666667 "fsharp A"
            Expect.equal (classic.Nc, classic.Na) (2, 0) "classic counts"
            expectClose classic.A 0.0 "classic A"
        }

        test "project level groups every namespace in a project into one component" {
            let api = concrete "App.Api" "Api"
            let domain = concrete "App.Domain" "Domain"
            let core = { concrete "Lib.Core" "Core" with Project = "Lib" }

            let results =
                calculate { options with Level = Level.Project } [ api; domain; core ]
                    [ api => domain; domain => core ]

            let app = results |> List.find (fun c -> c.Name = "App")
            let lib = results |> List.find (fun c -> c.Name = "Lib")
            Expect.equal (app.Nc, app.Ca, app.Ce) (2, 0, 1) "App"
            Expect.equal (lib.Nc, lib.Ca, lib.Ce) (1, 1, 0) "Lib"
        }

        test "module level splits modules out and leaves other declarations in their namespace" {
            let inModule = { concrete "App.Domain" "Price" with ModulePath = Some "App.Domain.Pricing" }
            let inNamespace = concrete "App.Domain" "Order"

            let results =
                calculate { options with Level = Level.Module } [ inModule; inNamespace ] [ inModule => inNamespace ]

            Expect.equal (componentNamed "App.Domain.Pricing" results).Ce 1 "module component"
            Expect.equal (componentNamed "App.Domain" results).Ca 1 "namespace component"
        }

        test "zones apply only beyond the distance threshold and on the correct side" {
            // threshold, (na, nc), (ca, ce), expected zone
            [ 0.5, (0, 1), (1, 0), Some Pain // A=0, I=0, D=1
              0.5, (1, 1), (0, 1), Some Uselessness // A=1, I=1, D=1
              0.5, (1, 4), (3, 1), None // A=0.25, I=0.25, D=0.5 is at the threshold, not beyond it
              0.5, (0, 1), (3, 1), Some Pain // A=0, I=0.25, D=0.75
              0.2, (1, 2), (1, 0), None // A=0.5 is not below 0.5
              0.2, (1, 1), (1, 1), None // A=1, I=0.5 is not above 0.5
              0.5, (0, 1), (0, 0), None ] // undefined distance
            |> List.iter (fun (threshold, abstractness, coupling, expected) ->
                let declarations, edges = shaped abstractness coupling

                let result =
                    calculate { options with DistanceThreshold = threshold } declarations edges
                    |> componentNamed "App.Subject"

                Expect.equal result.Zone expected $"threshold {threshold}, {abstractness}, {coupling}")
        }

        test "data share counts data declarations that are not abstract under the profile" {
            let ns = "App.Mixed"

            let declarations =
                [ { concrete ns "Order" with IsData = true }
                  { declaration Abstract ns "Repo" with ClassicAbstraction = Concrete; IsData = true }
                  concrete ns "Service"
                  { declaration NotCounted ns "Alias" with IsData = true } ]

            let share profile =
                (calculate { options with Profile = profile } declarations [] |> componentNamed ns).DataShare

            expectClose (share Profile.Functional) 0.3333333333 "functional: Repo is abstract, so only Order is data"
            expectClose (share Profile.Classic) 0.6666666667 "classic: Repo is concrete data"

            let nothingCounted =
                calculate options [ declaration NotCounted "App.Empty" "Alias" ] [] |> componentNamed "App.Empty"

            Expect.isNone nothingCounted.DataShare "undefined when Nc is 0"
        }

        test "an edge to a declaration outside the analysed set counts as efferent coupling" {
            let source = concrete "App.Api" "Source"
            let external = { From = source.Key; To = { Assembly = "SomePackage"; Path = "Some"; Name = "Thing" } }

            let results = calculate options [ source ] [ external ]

            Expect.equal (componentNamed "App.Api" results).Ce 1 "Ce"
            Expect.hasLength results 1 "the external target gets no component of its own"
        }

        test "summary averages defined distances and counts isolated components" {
            let row =
                { Name = "row"; Nc = 1; Na = 0; Ca = 0; Ce = 0
                  A = Some 0.0; I = Some 0.0; D = Some 0.0
                  DataShare = Some 0.0; Zone = None }

            let summary =
                summarize
                    [ { row with D = Some 0.2 }
                      { row with D = Some 0.8 }
                      { row with I = None; D = None } // isolated
                      { row with A = None; D = None } ] // nothing counted, but coupled

            Expect.equal summary.Components 4 "every component is counted"
            expectClose summary.MeanDistance 0.5 "undefined distances are left out"
            Expect.equal summary.Isolated 1 "only undefined instability is isolated"
        }

        test "summary has no mean distance when no component has one" {
            let summary = summarize []

            Expect.equal summary { Components = 0; MeanDistance = None; Isolated = 0 } "empty"
        }
    ]

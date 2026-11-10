module Ballast.Core.Metrics

[<RequireQualifiedAccess>]
type Level =
    | Project
    | Namespace
    | Module

[<RequireQualifiedAccess>]
type Profile =
    | Functional
    | Classic

type Options =
    { Level: Level
      Profile: Profile
      DistanceThreshold: float }

type Summary =
    { Components: int
      MeanDistance: float option
      Isolated: int }

let private componentName level (declaration: Declaration) =
    match level with
    | Level.Project -> declaration.Project
    | Level.Namespace -> declaration.Project + "::" + declaration.NamespacePath
    | Level.Module ->
        declaration.Project + "::" + (declaration.ModulePath |> Option.defaultValue declaration.NamespacePath)

let calculate (options: Options) (declarations: Declaration list) (edges: Edge list) : ComponentMetrics list =
    let nameOf = componentName options.Level
    let owner = declarations |> List.map (fun d -> d.Key, nameOf d) |> Map.ofList
    let ownerOf key = Map.tryFind key owner

    declarations
    |> List.groupBy nameOf
    |> List.map (fun (name, members) ->
        let abstraction (d: Declaration) =
            match options.Profile with
            | Profile.Functional -> d.FunctionalAbstraction
            | Profile.Classic -> d.ClassicAbstraction

        let nc = members |> List.filter (fun d -> abstraction d <> NotCounted) |> List.length
        let na = members |> List.filter (fun d -> abstraction d = Abstract) |> List.length
        let here = Some name
        let couplingSources (crossing: Edge -> bool) =
            edges |> List.filter crossing |> List.map (fun e -> e.From) |> List.distinct |> List.length

        let ca = couplingSources (fun e -> ownerOf e.To = here && ownerOf e.From <> here)
        let ce = couplingSources (fun e -> ownerOf e.From = here && ownerOf e.To <> here)

        let a = if nc = 0 then None else Some(float na / float nc)
        let i = if ca + ce = 0 then None else Some(float ce / float (ca + ce))
        let d = Option.map2 (fun a i -> abs (a + i - 1.0)) a i
        let data = members |> List.filter (fun d -> d.IsData && abstraction d = Concrete) |> List.length
        let dataShare = if nc = 0 then None else Some(float data / float nc)

        let zone =
            match a, i, d with
            | Some a, Some i, Some d when d > options.DistanceThreshold ->
                if a < 0.5 && i < 0.5 then Some Pain
                elif a > 0.5 && i > 0.5 then Some Uselessness
                else None
            | _ -> None

        { Name = name
          Nc = nc
          Na = na
          Ca = ca
          Ce = ce
          A = a
          I = i
          D = d
          DataShare = dataShare
          Zone = zone })

let summarize (components: ComponentMetrics list) : Summary =
    let distances = components |> List.choose (fun c -> c.D)

    { Components = components.Length
      MeanDistance = if distances.IsEmpty then None else Some(List.average distances)
      Isolated = components |> List.filter (fun c -> c.I.IsNone) |> List.length }

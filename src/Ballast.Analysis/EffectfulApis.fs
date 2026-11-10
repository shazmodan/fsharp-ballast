/// Framework APIs whose use marks code as effectful. Shared by the F# and C# extractors.
module Ballast.Analysis.EffectfulApis

let private namespaces =
    [ "System.IO"; "System.Net"; "System.Data"; "Microsoft.Data"; "Microsoft.EntityFrameworkCore" ]

/// Types inside those namespaces that only compute, such as Path, which manipulates strings.
let private pureTypes = set [ "System.IO.Path" ]

let private types =
    set
        [ "System.Console"
          "System.Random"
          "System.Environment"
          "System.Threading.Thread"
          "System.Diagnostics.Stopwatch"
          "System.Diagnostics.Process"
          "System.Net.Http.HttpClient" ]

let private members =
    set
        [ "System.DateTime.Now"
          "System.DateTime.UtcNow"
          "System.DateTime.Today"
          "System.DateTimeOffset.Now"
          "System.DateTimeOffset.UtcNow"
          "System.Guid.NewGuid"
          // F# console output usually goes through these rather than Console.
          "Microsoft.FSharp.Core.ExtraTopLevelOperators.printf"
          "Microsoft.FSharp.Core.ExtraTopLevelOperators.printfn"
          "Microsoft.FSharp.Core.ExtraTopLevelOperators.eprintf"
          "Microsoft.FSharp.Core.ExtraTopLevelOperators.eprintfn"
          "Microsoft.FSharp.Core.Operators.stdin"
          "Microsoft.FSharp.Core.Operators.stdout"
          "Microsoft.FSharp.Core.Operators.stderr" ]

/// The effectful API a use refers to, given the target type's namespace and name and, for a member use,
/// the member's name. None when the use is not effectful.
let classify (namespaceName: string) (typeName: string) (memberName: string option) : string option =
    let fullType = if namespaceName = "" then typeName else namespaceName + "." + typeName
    let fullMember = memberName |> Option.map (fun name -> fullType + "." + name)

    if pureTypes.Contains fullType then
        None
    elif fullMember |> Option.exists members.Contains then
        fullMember
    elif types.Contains fullType then
        Some fullType
    elif namespaces |> List.exists (fun n -> namespaceName = n || namespaceName.StartsWith(n + ".")) then
        Some fullType
    else
        None

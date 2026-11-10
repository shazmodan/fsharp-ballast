module Ballast.Cli.Program

[<EntryPoint>]
let main argv =
    Cli.run argv stdout stderr

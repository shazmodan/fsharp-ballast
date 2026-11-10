module Ballast.Tests.Fixtures

open System
open System.IO

let private fixturesRoot = Path.Combine(__SOURCE_DIRECTORY__, "..", "fixtures")

let rec private copyDirectory source target =
    Directory.CreateDirectory target |> ignore

    for file in Directory.GetFiles source do
        File.Copy(file, Path.Combine(target, Path.GetFileName file))

    for directory in Directory.GetDirectories source do
        copyDirectory directory (Path.Combine(target, Path.GetFileName directory))

/// Runs against a private copy of a fixture, so each run starts unrestored.
let withFixture name (run: string -> 'result) : 'result =
    let directory = Path.Combine(Path.GetTempPath(), "ballast-tests", Guid.NewGuid().ToString "N")
    copyDirectory (Path.Combine(fixturesRoot, name)) directory

    try
        run directory
    finally
        try
            Directory.Delete(directory, true)
        with _ ->
            ()

/// MSBuild evaluation is process-wide, so tests that load projects share this sequenced group.
[<Literal>]
let MsBuildGroup = "msbuild"

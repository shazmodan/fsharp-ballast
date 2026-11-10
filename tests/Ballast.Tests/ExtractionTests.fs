module Ballast.Tests.ExtractionTests

open Expecto
open Ballast.Core

let private declaration assembly name =
    { Key = { Assembly = assembly; Path = "Space"; Name = name }
      Project = assembly
      NamespacePath = "Space"
      ModulePath = None
      FunctionalAbstraction = Concrete
      ClassicAbstraction = Concrete
      IsData = false
      EffectSignals = [] }

let private (=>) (source: Declaration) (target: DeclarationKey) = { From = source.Key; To = target }

let private package = { Assembly = "SomePackage"; Path = "Some"; Name = "Thing" }

[<Tests>]
let tests =
    testList "Extraction.merge" [
        test "unites declarations and keeps edges that cross between extractions" {
            let fsharp = declaration "FsApp" "Main"
            let csharp = declaration "CsLib" "Order"

            let merged =
                Extraction.merge false [
                    { Declarations = [ fsharp ]; Edges = [ fsharp => csharp.Key ] }
                    { Declarations = [ csharp ]; Edges = [] }
                ]

            Expect.equal merged.Declarations [ fsharp; csharp ] "declarations"
            Expect.equal merged.Edges [ fsharp => csharp.Key ] "the cross-language edge"
        }

        test "keeps edges to external packages only when asked" {
            let source = declaration "App" "Source"
            let extraction = { Declarations = [ source ]; Edges = [ source => package ] }

            Expect.isEmpty (Extraction.merge false [ extraction ]).Edges "left out by default"
            Expect.equal (Extraction.merge true [ extraction ]).Edges [ source => package ] "kept on request"
        }

        test "drops edges to undeclared targets in an analysed assembly, even when asked for externals" {
            let source = declaration "App" "Source"
            let ghost = { Assembly = "App"; Path = "Space"; Name = "Ghost" }
            let extraction = { Declarations = [ source ]; Edges = [ source => ghost ] }

            Expect.isEmpty (Extraction.merge true [ extraction ]).Edges "not a declaration, not a package"
        }

        test "keeps each declaration's effect signals" {
            let writer = { declaration "App" "Writer" with EffectSignals = [ ReturnsUnit "Save"; MutableState "count" ] }

            let merged = Extraction.merge false [ { Declarations = [ writer ]; Edges = [] } ]

            Expect.equal merged.Declarations.Head.EffectSignals [ ReturnsUnit "Save"; MutableState "count" ] "signals"
        }

        test "removes duplicate edges and edges from a declaration to itself" {
            let first = declaration "App" "First"
            let second = declaration "App" "Second"

            let merged =
                Extraction.merge false [
                    { Declarations = [ first; second ]; Edges = [ first => second.Key; first => first.Key ] }
                    { Declarations = []; Edges = [ first => second.Key ] }
                ]

            Expect.equal merged.Edges [ first => second.Key ] "one edge"
        }
    ]

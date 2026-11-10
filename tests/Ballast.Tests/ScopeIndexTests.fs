module Ballast.Tests.ScopeIndexTests

open Expecto
open FSharp.Compiler.CodeAnalysis
open FSharp.Compiler.Text
open Ballast.Analysis

let private checker = FSharpChecker.Create()

let private parse (file: string) (source: string) =
    let options = { FSharpParsingOptions.Default with SourceFiles = [| file |]; IsExe = true }
    let result = checker.ParseFile(file, SourceText.ofString source, options) |> Async.RunSynchronously
    Expect.isFalse result.ParseHadErrors $"the test source should parse: {result.Diagnostics}"
    ScopeIndex.ofParseTree result.ParseTree

/// The range of the first occurrence of text in the source.
let private rangeOf (source: string) (text: string) =
    let lines = source.Split '\n'
    let line = lines |> Array.findIndex (fun l -> l.Contains text)
    let column = lines[line].IndexOf text
    Range.mkRange "Test.fs" (Position.mkPos (line + 1) column) (Position.mkPos (line + 1) (column + text.Length))

let private scopeAt source text =
    ScopeIndex.enclosing (parse "Test.fs" source) (rangeOf source text)

let private expectScope source text (name, kind) =
    let scope = Expect.wantSome (scopeAt source text) $"a scope at '{text}'"
    Expect.equal (scope.Name, scope.Kind) (name, kind) $"scope at '{text}'"

[<Tests>]
let tests =
    testList "ScopeIndex" [
        test "a location inside a type belongs to that type" {
            let source = "namespace App.Domain\n\ntype Order = { Id: int }\n"

            expectScope source "Id" ("App.Domain.Order", ScopeIndex.TypeScope)
        }

        test "a location in a top-level module belongs to that module" {
            let source = "module App.Main\n\nlet value = 1\n"

            expectScope source "value" ("App.Main", ScopeIndex.ModuleScope)
        }

        test "nested modules and the types inside them resolve to the innermost scope" {
            let source =
                "namespace App\n\nmodule Outer =\n    module Inner =\n        let deep = 1\n        type Thing = { Field: int }\n    let shallow = 2\n"

            expectScope source "deep" ("App.Outer.Inner", ScopeIndex.ModuleScope)
            expectScope source "Field" ("App.Outer.Inner.Thing", ScopeIndex.TypeScope)
            expectScope source "shallow" ("App.Outer", ScopeIndex.ModuleScope)
        }

        test "each type in an and group has its own scope" {
            let source = "namespace App\n\ntype First = { A: Second option }\nand Second = { B: int }\n"

            expectScope source "A:" ("App.First", ScopeIndex.TypeScope)
            expectScope source "B:" ("App.Second", ScopeIndex.TypeScope)
        }

        test "a file can declare several namespaces" {
            let source = "namespace App.One\n\ntype X = { OneField: int }\n\nnamespace App.Two\n\ntype Y = { TwoField: int }\n"

            expectScope source "OneField" ("App.One.X", ScopeIndex.TypeScope)
            expectScope source "TwoField" ("App.Two.Y", ScopeIndex.TypeScope)
        }

        test "recursive namespaces and modules are indexed like ordinary ones" {
            let inNamespace = "namespace rec App\n\ntype X = { Field: int }\n\nmodule M =\n    let value = 1\n"
            let inModule = "module rec App.Main\n\nlet value = 1\n"

            expectScope inNamespace "Field" ("App.X", ScopeIndex.TypeScope)
            expectScope inNamespace "value" ("App.M", ScopeIndex.ModuleScope)
            expectScope inModule "value" ("App.Main", ScopeIndex.ModuleScope)
        }

        test "a file with no declaration is an implicit module named after the file" {
            expectScope "let value = 1\n" "value" ("Test", ScopeIndex.ModuleScope)
        }

        test "a namespace header is outside every scope" {
            Expect.isNone (scopeAt "namespace App.Domain\n\ntype X = { Id: int }\n" "App.Domain") "no scope"
        }

        test "declarations in the global namespace have no namespace prefix" {
            expectScope "namespace global\n\ntype Thing = { Field: int }\n" "Field" ("Thing", ScopeIndex.TypeScope)
        }

        test "open declarations are recognised at namespace and module level" {
            let source = "namespace App\n\nopen System\n\nmodule M =\n    open System.IO\n    let value = 1\n"
            let index = parse "Test.fs" source

            Expect.isTrue (ScopeIndex.isInOpen index (rangeOf source "System")) "namespace-level open"
            Expect.isTrue (ScopeIndex.isInOpen index (rangeOf source "System.IO")) "module-level open"
            Expect.isFalse (ScopeIndex.isInOpen index (rangeOf source "value")) "ordinary code"
        }

        testList "an attribute belongs to the declaration it decorates" [
            test "on a type" {
                let source = "namespace App\n\n[<Struct>]\ntype Point = { X: int }\n"
                expectScope source "Struct" ("App.Point", ScopeIndex.TypeScope)
            }

            test "on a type in an and group" {
                let source = "namespace App\n\ntype A = { X: int }\nand [<Struct>] B = { Y: int }\n"
                expectScope source "Struct" ("App.B", ScopeIndex.TypeScope)
            }

            test "on a nested module" {
                let source = "namespace App\n\n[<AutoOpen>]\nmodule Helpers =\n    let value = 1\n"
                expectScope source "AutoOpen" ("App.Helpers", ScopeIndex.ModuleScope)
            }

            test "on a top-level module" {
                let source = "[<AutoOpen>]\nmodule App.Main\n\nlet value = 1\n"
                expectScope source "AutoOpen" ("App.Main", ScopeIndex.ModuleScope)
            }
        ]

        test "an intrinsic augmentation belongs to the type it extends" {
            let source = "namespace App\n\ntype Order = { Id: int }\n\ntype Order with\n    member this.Twice = this.Id * 2\n"

            expectScope source "Twice" ("App.Order", ScopeIndex.TypeScope)
        }

        testList "an optional extension belongs to the module that declares it" [
            test "with a qualified type name" {
                let source = "namespace App\n\nmodule Extensions =\n    type System.String with\n        member this.Shout = this.ToUpper()\n"
                expectScope source "Shout" ("App.Extensions", ScopeIndex.ModuleScope)
            }

            test "with an opened type name" {
                let source = "module App.Main\n\nopen System\n\ntype String with\n    member this.Shout = this.ToUpper()\n"
                expectScope source "Shout" ("App.Main", ScopeIndex.ModuleScope)
            }
        ]

        test "an exception declaration has its own type scope" {
            let source = "namespace App\n\nexception Failed of string\n"

            expectScope source "string" ("App.Failed", ScopeIndex.TypeScope)
        }

        test "a signature file has no scopes" {
            let source = "namespace App\n\ntype Order = { Id: int }\n"
            let index = parse "Test.fsi" source

            Expect.isNone (ScopeIndex.enclosing index (rangeOf source "Id")) "signature files are not analysed"
        }
    ]

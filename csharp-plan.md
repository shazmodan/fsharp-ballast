# Plan: C# support

Extend fsharp-ballast so it measures Abstractness, Instability and Distance from the Main Sequence for C# projects, and for solutions that mix C# and F#. The metrics, reports and CLI stay the same. What changes is how declarations and dependencies are found.

This continues `instructions.md`. Its milestone numbering continues from 8.

## 1. Goals and non-goals

Goals:

- C# projects in `.sln`, `.slnx` and `.csproj` inputs are analysed with the same metrics, levels, reports and exit codes as F#.
- In a mixed solution, a dependency between a C# and an F# declaration counts like any other.
- The F# behaviour and its 129 tests stay unchanged.

Not in scope: Visual Basic, cross-language analysis of reflection, and source generators beyond what section 5 describes.

## 2. Design decisions

| Topic | Decision | Reason |
| --- | --- | --- |
| C# analysis source | Roslyn semantic model, `Microsoft.CodeAnalysis.CSharp.Workspaces` 5.9.0 | The C# equivalent of FCS: it sees declared symbols, not compiled IL. |
| Loading C# projects | `MSBuildWorkspace` from `Microsoft.CodeAnalysis.Workspaces.MSBuild` 5.9.0, one project at a time | It resolves references, analysers and source generators correctly. Version 5.9.0 evaluates projects in an out-of-process build host, which should keep it apart from Ionide's in-process MSBuild (spike item 1). |
| Finding projects | Keep Ionide for listing a solution's projects, test-project detection and restore. Send `.fsproj` to FCS and `.csproj` to Roslyn | One place decides what is in the solution, and `.slnx` already works there. |
| Pipeline shape | Each language produces language-neutral `Declaration` and `Edge` values, then the extractions are merged before `Metrics.calculate` | `Ballast.Core` and the reports stay untouched, and classification moves next to the language that knows the symbols. |
| Declaration identity | The existing `DeclarationKey`. For C#: assembly name; containing namespace and containing types' `MetadataName`s as `Path`, or `global` when there are none; and `MetadataName` as `Name` | The spike confirmed these keys equal the ones FCS builds for the same C# types (section 8, item 3). |
| Mixed solutions, F# referencing C# | Compile the referenced C# project with Roslyn to an in-memory, metadata-only assembly, and give it to FCS as a referenced project | Keeps the "restore, never build" promise (spike item 4). |
| Mixed solutions, C# referencing F# | Not supported at first. Fail with a clear message, and suggest building first or excluding the project | Roslyn needs F# metadata, and producing it means compiling the F# project. Revisit after milestone 14. |
| Profile names | Add `functional` as an alias for the `fsharp` profile. Keep `fsharp` working | "fsharp" reads oddly on C# code, and renaming would break existing CI scripts. |

## 3. Architecture changes

```
.sln/.slnx/.fsproj/.csproj
   -> Load       Ionide: restore, list projects, drop test projects
        -> .fsproj  FCS:    check -> extract -> classify  -> Declaration, Edge
        -> .csproj  Roslyn: compile -> extract -> classify -> Declaration, Edge
   -> Merge      union of declarations and edges, filtered to the analysed set
   -> Metrics    unchanged
   -> Report     unchanged
```

- `Ballast.Analysis` keeps the F# code. A new `Ballast.Analysis.CSharp` project holds the Roslyn code, so F#-only users don't depend on Roslyn's analysis code paths. Both are packed into the tool.
- `SymbolExtractor.LocatedDeclaration` currently carries an `FSharpEntity` for `Classifier` to read. Classification moves into each language's extraction, so that `Extraction` contains `Ballast.Core.Declaration` directly.
- The rule "drop edges to targets outside the analysed set" moves from each extractor to the merge step, because only after merging is the whole analysed set known.

## 4. C# declarations and classification

A declaration is every named type in source: class, struct, interface, enum, delegate, record class and record struct. Nested types are separate declarations, as types nested in F# modules are. A partial type is one declaration, because Roslyn gives all its parts one symbol.

| C# construct | Detection | In `Nc`? | `fsharp`/`functional` | `classic` | `IsData` |
| --- | --- | --- | --- | --- | --- |
| Interface | `TypeKind.Interface` | Yes | Abstract | Abstract | No |
| Abstract class | `IsAbstract`, not `IsStatic` | Yes | Abstract | Abstract | No |
| Delegate | `TypeKind.Delegate` | Yes | Abstract | Concrete | No |
| Record whose properties are all delegates | `IsRecord`, every positional property is a delegate type | Yes | Abstract | Concrete | Yes |
| Other record class or record struct | `IsRecord` | Yes | Concrete | Concrete | Yes |
| Enum | `TypeKind.Enum` | Yes | Concrete | Concrete | Yes |
| Static class with at least one member | `IsStatic` | Yes | Concrete | Concrete | No |
| Static class containing only nested types | `IsStatic`, no members | No | Not counted | Not counted | No |
| Other class or struct, including attributes and exceptions | Otherwise | Yes | Concrete | Concrete | No |
| Synthesised `Program` for top-level statements | Containing type of the entry point | Yes | Concrete | Concrete | No |

The static-class rows mirror F# modules. `using` aliases such as `using Clock = System.Func<DateTime>;` are not declarations, so unlike F# abbreviations they are never counted.

Locations:

- `NamespacePath` is the containing namespace, or `<global>`.
- `ModulePath` is used by `--level module`. It is the outermost containing static class, and a static class is its own module. Other types have none, so they stay in their namespace's component.

## 5. C# dependencies

A dependency's source is the innermost type declaration around the syntax node, found by walking `Ancestors()`. Code outside any type, meaning top-level statements, belongs to the synthesised `Program`. C# needs no equivalent of the F# scope index.

| Syntax | Target |
| --- | --- |
| A type name anywhere: declarations, base lists, generic arguments and constraints, `typeof`, `nameof`, casts, patterns, attributes | The named type |
| A member access, invocation, object creation or property pattern | The member's `ContainingType` |
| An extension method call | The static class that declares the method, from `ReducedFrom` |
| An enum member | The enum |
| `var` locals, lambda parameters, deconstruction and pattern variables | The named types in the symbol's type, walked through generic arguments, arrays, pointers and tuples (the same idea as the F# `FullType` walk) |
| `using` directives, including `global using` and `using static` | Ignored, as F# `open` is |
| Type parameters, locals, `dynamic` | Ignored |
| `<see cref>` in documentation comments | Ignored. A comment is not a dependency |

Also:

- **Generated code:** syntax trees from source generators, files under `obj`, and files marked `<auto-generated>` are not analysed and declare nothing. Edges to types declared there are dropped, as framework edges are.
- **Implicit calls:** milestone 12 uses `GetSymbolInfo` and `GetTypeInfo` on syntax. That misses members the compiler calls without naming them: user-defined implicit conversions, operators, `foreach` enumerators, `await` awaiters, collection initialiser `Add` and deconstruction. Milestone 15 adds them through the `IOperation` tree.
- **Framework:** `FSharp.Core` and reference-pack assemblies never count, as now. NuGet packages count only with `--include-external`.

## 6. Mixed solutions

- **Cross-language keys:** an F# use of a C# type produces a key from FCS (`AccessPath` and `CompiledName`), and the C# declaration produces a key from Roslyn. They must be equal, or the edge is dropped as external. Milestone 14 tests this for namespaced, nested and generic types.
- **F# referencing C#:** emit the C# project with `EmitOptions(metadataOnly: true)` to a memory stream. Add it to the F# project's referenced projects as a `PEReference` whose `DelayedILModuleReader` is named with the C# reference-assembly path already in the F# `-r:` options. The spike confirmed this works with nothing built.
- **C# referencing F#:** the C# compilation reports missing metadata, so `ProjectLoader.check` fails with a message naming both projects and suggesting `dotnet build` first or `--exclude`. A later option is to build only the F# projects that C# references.

## 7. Milestones

Build each one test-first at seams agreed before the tests, as before.

**9. Spike. Done 2026-11-10.** A throwaway program checked every assumption in section 8 against small C# and F# fixtures and a generated 20-project C# solution. Results are in section 8, and this plan has been updated to match.

**10. Language-neutral pipeline.** Move classification into the F# extraction so that it returns `Declaration` values, and move analysed-set filtering into a merge step. No behaviour change. Acceptance: all 129 existing tests pass unchanged, apart from test code that builds its inputs.

**11. C# loading and checking.** The loader sends `.csproj` files to Roslyn through `MSBuildWorkspace`, and C# compile errors become `CompilationFailed` with file, line and error id. Acceptance: fixtures for a C# project, a C#-only `.slnx`, a C# type error and a C# project that cannot load.

**12. C# extraction.** Declarations, locations and edges for every row in section 5, apart from the implicit calls. Acceptance: a `CSharpDependencies` fixture with one source type per row, plus partial types, nested types, file-local types, top-level statements and generated code.

**13. C# classification.** Acceptance: one fixture declaration per row of the table in section 4, under both profiles, plus the `functional` alias.

**14. Mixed solutions.** F#-to-C# edges through the in-memory metadata assembly, cross-language keys, and the clear failure for C# referencing F#. Acceptance: a mixed fixture with a hand-calculated metrics table, as in `instructions.md` section 8, and a test for the C#-to-F# message.

**15. Implicit C# dependencies.** Walk `IOperation` for conversions, operators, `foreach`, `await`, collection initialisers and deconstruction. Acceptance: one fixture source type per case.

**16. CLI, reports and README.** Language-neutral wording in usage and README, the `functional` alias, and C# examples. Acceptance: the C# Layered example matches its table in all four formats.

**17. Real-world validation.** Run on a C# library, a C# application and a mixed solution, spot-check about 20 edges each, and record time and memory. Running other repositories' builds needs your approval, as it did for milestone 7. Acceptance: no crashes, and every wrong-looking edge is explained or fixed.

## 8. Spike results

Run on 2026-11-10 with FCS 43.12.201, Ionide.ProjInfo 0.75.3 and Roslyn 5.9.0 on SDK 10.0.114, against fixtures that had been restored but never built.

| # | Assumption | Result |
| --- | --- | --- |
| 1 | Roslyn and Ionide can load MSBuild in one process | Confirmed, but only with a fix. Roslyn's packaged `Microsoft.Build.Framework` 18.0.2 shadowed the SDK's copy and Ionide crashed with a `TypeLoadException`. Referencing that package with `ExcludeAssets="runtime"` makes both use the SDK's MSBuild. |
| 2 | Restored projects open without another restore, and `WorkspaceFailed` detects failures | Partly. Projects open and C# errors arrive with file, line and id, such as CS0029. But `WorkspaceFailed` never fired, even for an unrestored project, which still compiled. Restore detection must stay in our loader, whose assets-file check already covers `.csproj`. A malformed project is caught by restore and Ionide before Roslyn sees it. |
| 3 | FCS and Roslyn produce equal keys for C# types | Confirmed for namespaced, nested, generic and nested-in-generic types, using `MetadataName` for the type and its containing types. The one difference: FCS gives `global` as the path for the global namespace, so the C# key must use `global` too. |
| 4 | A Roslyn metadata-only image can be an in-memory FCS reference | Confirmed: the F# project type-checks against the C# library with 0 errors, against 14 without the image, and nothing is built. Ionide adds no referenced project for a `.csproj`, only a `-r:` option for the C# reference assembly (`obj/.../ref/X.dll`). The in-memory reader must be named with exactly that path, with no extra `-r:` option. |
| 5 | Cost for about 20 C# projects is in line with F# | Better than F#. 20 chained projects, 19,380 lines and 143,960 symbol lookups took 50 s in total, with a 352 MB peak. Of that, 24 s was Ionide listing the projects and 14 s Roslyn opening them. The F# equivalent took 81–87 s and 2.1 GB. `MSBuildWorkspace.OpenSolutionAsync` also opened the `.slnx` file directly. |

Corrections the spike forced:

- Pin `Microsoft.Build.Framework` 18.0.2 with `ExcludeAssets="runtime"` in `Directory.Packages.props`, beside the Roslyn packages. Without it, the tool crashes as soon as it loads a project.
- The C# key uses `global` as the path for types in the global namespace.
- Each C# project's documents include generated files under `obj`, such as `GlobalUsings.g.cs`, so the filter in section 5 is needed from milestone 11.
- In mixed solutions, the in-memory image replaces the reference-assembly path found in the F# project's `-r:` options.

Not tested in the spike, and left to milestones 11 to 14: source generators, `file`-local types, top-level statements, multi-targeted C# projects, and C# projects that reference F# projects.

## 9. Decisions for you

- **C# referencing F#.** Accept a clear failure at first, as planned, or build the referenced F# projects automatically and give up "never builds" for that case.
- **Profile alias.** Add `functional` as planned, or rename the `fsharp` profile outright.
- **Tool name.** The tool is called `fsharp-ballast`. Keep it, or rename it once it measures C# too.

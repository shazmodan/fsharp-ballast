# fsharp-ballast: build plan

Build a command-line tool that measures Robert C. Martin's package metrics for F# code: **Abstractness (A)**, **Instability (I)** and **Distance from the Main Sequence (D)**.

The tool analyses a solution or project, reports the metrics per component, and can fail a CI build when a component drifts too far from the main sequence.

## 1. Metric definitions

For each component:

| Symbol | Name | Definition |
| --- | --- | --- |
| `Nc` | Type count | Number of counted declarations in the component (section 5) |
| `Na` | Abstract count | Number of those that are abstract (section 5) |
| `Ca` | Afferent coupling (fan-in) | Number of declarations **outside** the component that depend on something inside it |
| `Ce` | Efferent coupling (fan-out) | Number of declarations **inside** the component that depend on something outside it |
| `A` | Abstractness | `Na / Nc`, from 0 (concrete) to 1 (abstract) |
| `I` | Instability | `Ce / (Ca + Ce)`, from 0 (stable) to 1 (unstable) |
| `D` | Distance | `abs(A + I - 1)`, from 0 (on the main sequence) to 1 (furthest away) |

`Ca` and `Ce` count declarations, not dependency edges. A declaration that uses five things in another component counts once.

Undefined values:

- If `Ca + Ce = 0`, `I` is undefined. Report `null` and mark the component as `isolated`.
- If `Nc = 0`, `A` is undefined. Report `null`.
- If `A` or `I` is undefined, `D` is `null`. Exclude these components from averages and threshold checks.

Zones, reported only when `D` exceeds the distance threshold (default `0.5`):

- **Zone of Pain:** `A < 0.5` and `I < 0.5`. Concrete and depended upon.
- **Zone of Uselessness:** `A > 0.5` and `I > 0.5`. Abstract and unused.

## 2. Design decisions

| Topic | Decision | Reason |
| --- | --- | --- |
| Analysis source | F# Compiler Service (FCS) typed symbols, not compiled IL | IL contains closures, union case classes and other generated types that would distort `Nc` and coupling. FCS sees what the author wrote. |
| Project loading | `Ionide.ProjInfo` and `Ionide.ProjInfo.FCS` | Evaluates MSBuild properly, including file order, references and defines. |
| Component level | `--level project\|namespace\|module`, default `namespace` | Martin's component is a deployable unit, but many F# applications are one assembly, where project level gives a single meaningless row. |
| Component identity | Project name plus namespace or module path | A namespace split across two assemblies is deployed as two units. |
| Nesting | Flat. `App.Domain` does not include `App.Domain.Orders` | Keeps the numbers simple to explain. Same approach as NDepend. |
| External dependencies | Excluded by default. `--include-external` counts NuGet packages | Counting `FSharp.Core` and the BCL would push every component towards `I = 1`. BCL and `FSharp.Core` stay excluded even with the flag. |
| Test projects | Excluded by default. `--include-tests` keeps them | Tests inflate `Ca` for everything they touch. Detect with the MSBuild `IsTestProject` property. |
| Abstractness | `--profile fsharp\|classic`, default `fsharp` | F# expresses abstraction with function types and records of functions as well as interfaces. `classic` allows comparison with OO tools. |
| Visibility | Private and internal declarations are counted | Matches Martin's definition, which counts all classes. |
| Signature files | `.fsi` files are not analysed in v1, and do not count as abstraction | An implementation must conform to its signature, so the `.fs` file carries the dependencies. |

## 3. Technology

Pin these versions. They are the current stable releases that work together, as of 2026-11-10:

| Package | Version | Note |
| --- | --- | --- |
| Target framework | `net10.0` | SDK 10.0.114 is installed |
| `FSharp.Compiler.Service` | `43.12.201` | Exact version required by Ionide 0.75.3. Do not take 43.13.x until Ionide moves. |
| `FSharp.Core` | `10.1.201` | Same reason. Do not take 11.x. |
| `Ionide.ProjInfo`, `Ionide.ProjInfo.FCS` | `0.75.3` | |
| `Argu` | `6.2.5` | CLI parsing |
| `Expecto` | `11.1.1` | Tests |

## 4. Architecture

```
.sln/.slnx/.fsproj
   -> Load      Ionide: restore, evaluate, produce FSharpProjectOptions (order not guaranteed)
   -> Scope     per file: parse, then build an index of declaration ranges
   -> Extract   per project: typed symbol uses, attributed to declarations
   -> Graph     declarations + edges, grouped by component level
   -> Metrics   Nc, Na, Ca, Ce, A, I, D
   -> Report    table, JSON, CSV, HTML plot; exit code
```

Keep the metric code pure and free of FCS types, so it can be tested without running the compiler.

```
fsharp-ballast.slnx
src/Ballast.Core/         Model.fs, Metrics.fs (no FCS dependency)
src/Ballast.Analysis/     ProjectLoader.fs, ScopeIndex.fs, Classifier.fs, SymbolExtractor.fs, Pipeline.fs
src/Ballast.Cli/          Args.fs, Reports.fs, Program.fs (packed as a dotnet tool)
tests/Ballast.Tests/
tests/fixtures/           small real F# projects with hand-calculated results
```

Core model:

```fsharp
// Path = FSharpEntity.AccessPath, Name = FSharpEntity.CompiledName (includes generic arity, e.g. Box`1)
type DeclarationKey = { Assembly: string; Path: string; Name: string }

type Abstraction = Abstract | Concrete | NotCounted

type Declaration =
    { Key: DeclarationKey
      Project: string
      NamespacePath: string
      ModulePath: string option
      FSharpAbstraction: Abstraction
      ClassicAbstraction: Abstraction
      IsData: bool }

type Edge = { From: DeclarationKey; To: DeclarationKey }

type ComponentMetrics =
    { Name: string
      Nc: int; Na: int; Ca: int; Ce: int
      A: float option; I: float option; D: float option
      DataShare: float option
      Zone: Zone option }
```

### Scope index

FCS reports where a symbol is used but not which declaration contains that use. Build the mapping from the untyped syntax tree (`FSharpParseFileResults.ParseTree`):

1. Walk `SynModuleOrNamespace`, `SynModuleDecl.NestedModule` and `SynModuleDecl.Types`, and record each declaration's range. Each `SynTypeDefn` in an `and` group gets its own range.
2. Find the innermost scope containing a symbol use's range. That scope is the source of the edge.
3. Link a scope to its `FSharpEntity` through the definition symbol use inside it (`IsFromDefinition`). Use `ImplementationLocation`, because `DeclarationLocation` points at the `.fsi` for the symbol reported from a signature file. `GetAllUsesOfAllSymbols()` reports definitions from both files, so discard every use whose range is in an `.fsi` file.
4. Record `SynModuleDecl.Open` ranges. Symbol uses inside them are ignored, otherwise an unused `open` would create a dependency.
5. Treat an intrinsic augmentation (`type X with ...`) as belonging to the entity `X`. Treat an optional extension in another module as belonging to that module.
6. Drop any use outside all scopes, such as assembly-level attributes, and log a debug count.

### Mapping a symbol use to its target

Call `checker.ParseAndCheckProject` once per project with a single shared `FSharpChecker`, then read `GetAllUsesOfAllSymbols()`. Create the checker with `projectCacheSize` at least equal to the number of projects, and do not clear its cache between projects (section 9). Project references resolve from source, so the analysed solution does not need to be built, only restored.

| Symbol | Target declaration |
| --- | --- |
| `FSharpEntity` that is a namespace | Ignored |
| `FSharpEntity`, including an abbreviation | The entity itself |
| `FSharpMemberOrFunctionOrValue` | `DeclaringEntity`. Local values have none, so ignore the target but still walk the type. |
| `FSharpUnionCase` | `DeclaringEntity` |
| `FSharpField` | `DeclaringEntity`. Anonymous record fields have none and are ignored. |
| `FSharpActivePatternCase` | `Group.DeclaringEntity` |
| `FSharpGenericParameter` | Ignored |

Inferred types leave no symbol use in the source. For example, `let g () = h ()` names only `h`, yet `g` also depends on what `h` returns. To catch these, for every definition of a function or value, walk `FullType` recursively:

- Skip the type if `IsGenericParameter` is true.
- Add `TypeDefinition` as a target only if `HasTypeDefinition` is true. Function and tuple types have none.
- Recurse through `GenericArguments`. This handles functions, tuples, generics, anonymous records and units of measure.
- Do not expand `AbbreviatedType`. The source depends on the abbreviation, and the abbreviation's own declaration carries the edge to its target.

Then build the edge set:

- Key every declaration and target by assembly simple name, `AccessPath` and `CompiledName`. Do not use `TryFullName` or `FullName`, because `TryFullName` returns `None` for every type abbreviation, including user-defined ones such as `Clock`.
- Find a declaration's namespace by walking up `DeclaringEntity` until `Namespace` is `Some`. `Namespace` is `None` on anything nested in a module. A top-level module with no namespace goes in a component named `<global>`.
- Drop self-edges.
- Drop edges to targets outside the analysed set, unless `--include-external` applies.
- Exclude generated files, meaning anything under `obj/`.

## 5. Abstractness classification

`IsData` is true for records, unions and enums. `DataShare` then counts only those that are `Concrete` under the selected profile.

| F# construct | Detection | In `Nc`? | `fsharp` profile | `classic` profile |
| --- | --- | --- | --- | --- |
| Interface | `IsInterface` | Yes | Abstract | Abstract |
| Abstract class | `IsAbstractClass` | Yes | Abstract | Abstract |
| Function abbreviation, `type Clock = unit -> DateTime` | `IsFSharpAbbreviation` and `AbbreviatedType.IsFunctionType` | `fsharp` only | Abstract | Not counted |
| Other abbreviation, `type Id = int` | `IsFSharpAbbreviation` | No | Not counted | Not counted |
| Record with at least one field, all function-typed | `IsFSharpRecord`, check `FSharpFields` | Yes | Abstract | Concrete |
| Delegate | `IsDelegate` | Yes | Abstract | Concrete |
| Other record, union, enum, struct, class, exception | Otherwise | Yes | Concrete | Concrete |
| Module with at least one value or function | `IsFSharpModule` | Yes | Concrete | Concrete |
| Module containing only other declarations | `IsFSharpModule` | No | Not counted | Not counted |
| Unit of measure | `IsMeasure` | No | Not counted | Not counted |

A `NotCounted` declaration is still a source and target of edges. It is excluded only from `Nc` and `Na`.

**`DataShare`** is the fraction of `Nc` where `IsData` is true. It does not affect any score. A package of immutable records in the Zone of Pain is much less worrying than one full of concrete services, because changing data shapes is its only real risk. The report shows this column so the reader can make that call.

## 6. Command-line interface

```
fsharp-ballast <path-to-sln-slnx-or-fsproj>
    [--level project|namespace|module]     default: namespace
    [--profile fsharp|classic]             default: fsharp
    [--format table|json|csv|html]         default: table
    [--output <file>]
    [--include-tests]
    [--include-external]
    [--exclude <glob>]                     repeatable, matched on component name
    [--configuration <name>]               default: Debug
    [--framework <tfm>]                    default: first target framework
    [--no-restore]
    [--distance-threshold <0..1>]          default: 0.5, used for zones
    [--max-distance <0..1>]                fail if any component exceeds this
```

Exit codes: `0` success, `1` `--max-distance` exceeded, `2` analysis failed (load failure, or any diagnostic with `FSharpDiagnosticSeverity.Error`). Do not rely on `HasCriticalErrors`, which stays false for ordinary type errors.

Sort table output by `D`, highest first. Compute with unrounded `float` and round to three decimals for display only. JSON shape:

```json
{
  "level": "namespace",
  "profile": "fsharp",
  "components": [
    { "name": "App::App.Domain", "nc": 2, "na": 0, "ca": 1, "ce": 0,
      "a": 0.0, "i": 0.0, "d": 1.0, "dataShare": 0.5, "zone": "pain" }
  ],
  "summary": { "components": 3, "meanDistance": 0.333, "isolated": 0 }
}
```

The HTML report is one self-contained page with an inline SVG scatter plot of `I` against `A`, the main sequence line, and shaded zones.

## 7. Milestones

Build each milestone test-first. Each one is done when its acceptance check passes.

**0. Spike. Done 2026-11-10.** Throwaway program that loaded fixtures through Ionide and printed every symbol use with its containing range. Results are in section 9, and this plan has been updated to match.

**1. Scaffold and pure metrics. Done 2026-11-10.** Solution, centrally pinned packages (`Directory.Packages.props`), and `Metrics.calculate` and `Metrics.summarize` tested over hand-built declarations and edges. `Ballast.Analysis` and `Ballast.Cli` are created in milestones 2 and 6, when they first have code. `calculate` counts every edge it receives, so extraction must drop external targets unless `--include-external` applies.

**2. Project loading. Done 2026-11-10.** `ProjectLoader.load` handles `.fsproj`, `.sln` and `.slnx`, restores first, and leaves C# projects out. `ProjectLoader.check` type-checks every project. Failures come back as a typed `AnalysisError` with a clear message, and milestone 6 maps them to exit code 2. Do not assume the options come back in build order, because `.slnx` returned a dependent project before its dependency. Ionide evaluates an unrestored project without reporting a failure and `RestoreSuccess` stays true, so the loader checks for the project's assets file. An F# project that references a C# project has not been tested.

**3. Scope index.** Acceptance: tests cover namespaces, top-level and nested modules, implicit file modules, `and` groups, `namespace rec`, augmentations, `open` ranges, and attributes on declarations.

**4. Symbol extraction.** Acceptance: one fixture per row of the target table in section 4, plus inferred return types, active patterns, extension methods, `inline` functions, SRTP, computation expressions and a file with an `.fsi` signature.

**5. Classification.** Acceptance: one fixture declaration per row of the table in section 5, asserted under both profiles.

**6. CLI and reports.** Acceptance: the end-to-end fixtures in section 8 match exactly in every output format, and all three exit codes are covered.

**7. Real-world validation.** Run against this repository and at least two open-source F# projects of different shapes, such as a library and an application. Spot-check about 20 edges by hand per project, and record run time and memory. Acceptance: no crashes, and any edge that looks wrong is explained or fixed.

**8. Packaging.** Set `PackAsTool` with the command name `fsharp-ballast`, and write a README covering the metrics, flags and CI use. Acceptance: `dotnet tool install` from a local package works, and the installed tool analyses a fixture.

## 8. Hand-calculated fixtures

### `Layered`, three namespaces in one project, namespace level

```fsharp
namespace Fixture.Ports
type IStore = abstract Save : string -> unit
type INotifier = abstract Notify : string -> unit

namespace Fixture.Domain
type Order = { Id: int }
module Pricing =
    let price (o: Order) = o.Id * 2

namespace Fixture.App
open Fixture.Ports
open Fixture.Domain
type Service(store: IStore) =
    member _.Run() = store.Save (string (Pricing.price { Id = 1 }))
module Main =
    let run (n: INotifier) (s: Service) = s.Run(); n.Notify "done"
```

| Component | Nc | Na | Ca | Ce | A | I | D | Zone |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| `Fixture.Ports` | 2 | 2 | 2 | 0 | 1.000 | 0.000 | 0.000 | none |
| `Fixture.Domain` | 2 | 0 | 1 | 0 | 0.000 | 0.000 | 1.000 | Pain |
| `Fixture.App` | 2 | 0 | 0 | 2 | 0.000 | 1.000 | 0.000 | none |

`Domain` has `Ca = 1` because only `Service` uses it. `Main` depends on `Service` and `INotifier`, and the first is inside its own component.

### `FunctionalPorts`, one namespace

```fsharp
namespace Fixture.Functional
open System
type Clock = unit -> DateTime
type Repo = { Load: int -> string; Save: string -> unit }
type Order = { Id: int }
```

| Profile | Nc | Na | A |
| --- | --- | --- | --- |
| `fsharp` | 3 | 2 | 0.667 |
| `classic` | 2 | 0 | 0.000 |

### Also add

- `TwoProjects`: the same dependency across a project reference, checked at project and namespace level.
- `UnusedOpen`: an `open` with no usage creates no edge.
- `Isolated`: a component with no couplings reports `I = null`, `D = null` and is left out of the mean.

## 9. Spike results

Run on 2026-11-10 with the pinned packages and SDK 10.0.114, against fixtures that had been restored but never built.

| # | Assumption | Result |
| --- | --- | --- |
| 1 | `LoadSln` accepts `.slnx` | Confirmed. `.sln` and `.slnx` gave identical symbol counts, but different project order. |
| 2 | `FCS.mapManyOptions` resolves project references with the right assembly name | Confirmed, with no compiled output present. |
| 3 | `SynTypeDefn.Range` includes leading attributes | Confirmed, for the first type in a group and for `and [<Attr>]`. Attributes on nested modules were not tested, so milestone 3 must cover them. |
| 4 | `IsAbstractClass` is true for `[<AbstractClass>]` | Confirmed. It is false for a class whose abstract slots all have defaults, which is correct. A class with abstract slots, no defaults and no attribute does not compile (FS0054, FS0365), so no rule is needed. |
| 5 | A use of an abbreviation resolves to the abbreviation | Confirmed, including across project references. |
| 6 | Acceptable speed and memory at about 20 projects | Acceptable for CI or on-demand runs, but memory is high. See below. |

Corrections the spike forced, already applied above:

- `TryFullName` returns `None` for abbreviations, so declarations are keyed on `AccessPath` and `CompiledName`. Tested on abbreviations, generic overloads by arity, nested modules and cross-project uses.
- `HasCriticalErrors` is false for ordinary type errors, so failure is detected from diagnostics.
- Project order from Ionide is not guaranteed, so nothing may depend on it.
- `FSharpEntity.Namespace` is `None` for declarations inside modules, so the namespace is found by walking up the parents.

Performance on a generated worst-case solution: 20 projects in a single reference chain, 300 files, 17,950 lines, 142,615 symbol uses. About 18 seconds of each run is project loading.

| Configuration | Total time | Peak working set |
| --- | --- | --- |
| `projectCacheSize = 200`, load order | 87 s | 2.1 GB |
| `projectCacheSize = 200`, dependency order | 81 s | 2.1 GB |
| `projectCacheSize = 200`, `ClearCache` after each project | 120 s | 3.1 GB |
| `projectCacheSize = 3`, dependency order | Not finished after 280 s | Not measured |

Keep the cache large and never clear it. The synthetic solution is one data point, so milestone 7 must record time and memory on real code. If memory is a problem there, try analysing against built assemblies instead of in-memory project references. That option has not been tested.

Not tested in the spike, and left to the milestone 3 and 4 fixtures: implicit file modules, top-level modules with no namespace, `namespace rec`, augmentations, active patterns, extension methods, `inline` and SRTP, and computation expressions.

## 10. Known limitations and non-goals

- Dependencies created through reflection, or through type providers' generated members beyond what FCS exposes, are not seen.
- Inferred types are captured only through the signatures of named functions and values. An intermediate expression's type that is never named or bound is missed.
- Only one target framework and one configuration are analysed per run, so `#if` branches that are inactive are invisible.
- Not in v1: cycle detection, Stable Dependencies Principle violations, trend tracking, `.fsi`-aware abstractness, and IDE integration.

## 11. Prerequisites

- The project needs a git repository and GitHub remote before the issue workflow in `docs/agents/issue-tracker.md` can be used.
- The five triage labels in `docs/agents/triage-labels.md` must then be created with `gh label create`.

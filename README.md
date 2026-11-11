# fsharp-ballast

A command-line tool that measures Robert C. Martin's package metrics for F# and C# code: **Abstractness**, **Instability** and **Distance from the Main Sequence**, plus **effectfulness**. Point it at a project or solution and it reports how each component balances stability against abstraction, and which ones sit in the Zone of Pain or the Zone of Uselessness.

It reads F# through the F# compiler and C# through Roslyn, so it sees the declarations you wrote rather than the extra types the compilers generate. A solution can mix both languages.

## Install

The package is not on NuGet yet. Until it is, build it from a clone of this repository and install it from the local package:

```
dotnet pack src/Ballast.Cli -c Release -o ./nupkg
dotnet tool install --global fsharp-ballast --add-source ./nupkg
```

Or as a local tool in another repository, with the path to that `nupkg` folder:

```
dotnet new tool-manifest
dotnet tool install fsharp-ballast --add-source <path to nupkg>
dotnet fsharp-ballast MySolution.sln
```

To pick up changes, pack again and run `dotnet tool update --global fsharp-ballast --add-source ./nupkg`. Raise the version in `src/Ballast.Cli/Ballast.Cli.fsproj` first, because NuGet caches a package by its version.

It needs the .NET SDK your projects build with, because it evaluates them with MSBuild. It restores them first, but never builds them.

## Usage

```
fsharp-ballast MySolution.slnx
fsharp-ballast src/MyApp/MyApp.fsproj --format html --output metrics.html
fsharp-ballast MySolution.sln --level project --max-distance 0.7
```

```
Component                   Nc  Na  Ca  Ce      A      I      D      E   Data  Zone
EffectZones::Fixture.Pure    2   0   1   0  0.000  0.000  1.000  0.000  0.500
EffectZones::Fixture.Store   2   0   1   0  0.000  0.000  1.000  1.000  0.000  pain
EffectZones::Fixture.App     1   0   0   1  0.000  1.000  0.000  1.000  0.000

3 components, mean distance 0.667, 0 isolated
```

`Pure` and `Store` are both stable and concrete, but only `Store` has side effects, so only `Store` is in the Zone of Pain.

## What the numbers mean

For each component:

| Column | Meaning |
| --- | --- |
| Nc | Declarations counted: types, and modules that contain values or functions |
| Na | How many of those are abstract |
| Ca | Afferent coupling: declarations outside the component that depend on it |
| Ce | Efferent coupling: declarations inside the component that depend on something outside it |
| A | Abstractness, `Na / Nc`. 0 is fully concrete, 1 fully abstract |
| I | Instability, `Ce / (Ca + Ce)`. 0 is maximally stable, 1 maximally unstable |
| D | Distance from the main sequence, `abs(A + I - 1)`. 0 is ideal |
| E | Effectfulness: share of counted declarations that are concrete and have side effects (see below) |
| Data | Share of counted declarations that are concrete records, unions or enums |
| Zone | `pain` or `uselessness` when D is beyond the distance threshold, as described below |

A component is either stable and abstract, so others can depend on it safely, or unstable and concrete, so it is easy to change. The **Zone of Pain** holds concrete components that many others depend on, which are hard to change. The **Zone of Uselessness** holds abstract components nothing depends on.

In functional code, stable and concrete is only risky when it comes with side effects. A stable module of pure functions or immutable records behaves like `String`: it rarely needs to change. So under the default `functional` profile, a component is only in the Zone of Pain when its E is also beyond the effect threshold, 0.25 by default. Under `--profile classic`, effects are ignored and the zones follow Martin's original definition.

A value is shown as `-` when it is undefined. Instability needs at least one dependency in or out, and abstractness needs at least one counted declaration. Undefined distances are left out of the mean.

### What counts as abstract

With the default `functional` profile, these count as abstract: interfaces, abstract classes, type abbreviations of function types such as `type Clock = unit -> DateTime`, records whose fields are all functions, delegates, and free-monad instruction unions such as `ReadUser of UserId * (User -> 'next)`. With `--profile classic`, only interfaces and abstract classes do, which matches object-oriented tools.

#### F# examples

Each comment gives the classification under `functional`, then under `classic`. Every example was checked by running the tool on it.

Abstract under both profiles:

```fsharp
// 1. An interface. Abstract / abstract.
type IUserStore =
    abstract Find: int -> string option
    abstract Save: int -> string -> unit

// 2. An abstract class, even with some implemented members. Abstract / abstract.
[<AbstractClass>]
type Shape() =
    abstract Area: float
    member shape.Describe() = $"Area {shape.Area}"
```

Abstract only under `functional`, because a function type is the functional way to state a contract:

```fsharp
// 3. An abbreviation of a function type. Abstract / not counted.
type Clock = unit -> System.DateTime

// 4. A record whose fields are all functions, a record of ports. Abstract / concrete.
type UserPorts =
    { Find: int -> string option
      Save: int -> string -> unit }

// 5. A delegate. Abstract / concrete.
type Validator = delegate of string -> bool

// 6. A free-monad instruction union. Every case ends in 'next, and at least one
//    case ends in a function returning it. Abstract / concrete.
type Instruction<'next> =
    | ReadUser of int * (string -> 'next)
    | WriteLog of string * 'next
```

Concrete under both profiles:

```fsharp
// 7. A record of data. Concrete, counted as data.
type User = { Id: int; Name: string }

// 8. A union of data. Concrete, counted as data.
type Payment =
    | Cash
    | Card of number: string

// 9. An enum. Concrete, counted as data.
type Level =
    | Low = 0
    | High = 1

// 10. A class with an implementation. Concrete, and effectful because of its mutable field.
type Counter() =
    let mutable count = 0
    member _.Increment() = count <- count + 1
    member _.Count = count

// 11. A module with functions. Concrete.
module Pricing =
    let discount (rate: decimal) (price: decimal) = price * (1m - rate)

// 12. A record that mixes data and functions. One plain field makes it concrete,
//     counted as data.
type Service =
    { Name: string
      Run: unit -> unit }

// 13. The program type that wraps instructions. Its Pure case ends in 'a, not
//     in a function, so it is concrete, counted as data.
type Program<'a> =
    | Free of Instruction<Program<'a>>
    | Pure of 'a

// 14. A union whose cases all end in 'next, but none in a function. Concrete,
//     counted as data.
type Step<'next> =
    | Start of 'next
    | Stop of 'next
```

Not counted under either profile, so they change neither Nc nor A:

```fsharp
// 15. An abbreviation of a type that is not a function.
type UserId = int

// 16. A unit of measure.
[<Measure>]
type cm

// 17. A module that only holds types. The module is not counted, but OrderId is
//     counted on its own as a concrete data type.
module Types =
    type OrderId = OrderId of int
```

C# follows the same rules: interfaces and abstract classes, including abstract records, are abstract under both profiles, while delegates and records whose positional parameters are all delegates, such as `record Repo(Func<int, string> Load)`, are abstract only under `functional`. A static class counts like an F# module: it is a concrete declaration if it has members, and is not counted if it only holds nested types. Records and enums count as data.

### What counts as effectful

F# and C# don't track side effects, so the tool looks for signals in each concrete declaration's own members:

- a function or method that returns `unit` or `void`
- a function or method that returns `Async`, `Task` or `ValueTask`
- an F# module function that takes `()`, such as `let now () = ...`
- mutable state: `let mutable`, `mutable` record fields, `ref` cells, settable properties and non-readonly fields, except on attribute classes, whose settable properties are named arguments
- a use of an effectful framework API: `System.IO` (except `Path`), `System.Net`, data access, a member of `Console`, `Random`, `Environment` (except `NewLine`), `Thread`, `Stopwatch` or `Process`, `DateTime.Now`, `Guid.NewGuid`, or F#'s `printf` family

One signal is enough to make a declaration effectful. Interfaces and other abstractions are never effectful, because hiding an effect behind one is exactly the point. Effects are not inherited: calling an effectful module does not make the caller effectful.

### What counts as a dependency

A declaration depends on another when its code names it, or uses one of its members, union cases, fields or active patterns. Types that are only inferred are included too, through the signatures of named functions and values. Unused `open` declarations create no dependency.

In C#, a type depends on whatever its code names or uses, including base types, attributes, `typeof`, `nameof`, extension methods and the inferred types of `var` and lambda parameters. Members the compiler calls without the code naming them count too: implicit conversions, operators, `foreach` enumerators, `await` awaiters, collection-initializer `Add` methods and `Deconstruct`. `using` directives and documentation comments create no dependency, and generated code is left out.

Dependencies on the .NET libraries and FSharp.Core never count. Dependencies on NuGet packages count only with `--include-external`.

In a mixed solution, an F# project that references a C# project is analysed without building anything, and dependencies across the language boundary count like any other. A C# project that references an F# project is not supported yet and stops the analysis with an error.

## Options

| Option | Effect |
| --- | --- |
| `--level project\|namespace\|module` | What counts as a component. Default: `namespace` |
| `--profile functional\|classic` | What counts as abstract. Default: `functional` |
| `--format table\|json\|csv\|html` | Report format. Default: `table` |
| `--output <file>` | Write the report to a file instead of the console |
| `--include-tests` | Include test projects, which are left out by default |
| `--include-external` | Count dependencies on NuGet packages as coupling |
| `--exclude <glob>` | Leave matching components out of the report. Repeatable |
| `--configuration <name>` | Build configuration to analyse. Default: `Debug` |
| `--framework <tfm>` | Target framework to analyse. Default: the project's first |
| `--no-restore` | Do not run `dotnet restore` first |
| `--distance-threshold <0..1>` | Distance beyond which a component gets a zone. Default: `0.5` |
| `--max-distance <0..1>` | Exit with code 1 if any component's distance exceeds this |
| `--effect-threshold <0..1>` | Under the `functional` profile, effectfulness beyond which a stable concrete component is in the Zone of Pain. Default: `0.25` |
| `--fail-on-zone pain\|uselessness` | Exit with code 1 if any component is in this zone. Repeatable |

Test projects are recognised by the MSBuild `IsTestProject` property, which `Microsoft.NET.Test.Sdk` sets. A test project without it is analysed unless you exclude it. `--exclude` only hides components from the report: their dependencies still count in other components' numbers.

The HTML report is a single self-contained page with a plot of every component against the main sequence and the full table. It works offline and in light or dark mode.

## Use in CI

```
fsharp-ballast MySolution.sln --fail-on-zone pain --format html --output metrics.html
```

| Exit code | Meaning |
| --- | --- |
| 0 | Success |
| 1 | A component's distance exceeds `--max-distance`, or a component is in a zone named by `--fail-on-zone`. The report is still written, and the components are listed on standard error |
| 2 | Invalid arguments, or the analysis failed: a project could not be loaded or has compile errors |

## Limitations

- One configuration and one target framework are analysed per run, so code in inactive `#if` branches is not seen.
- Dependencies created only through reflection are not seen.
- Signature files (`.fsi`) are not analysed separately and do not count as abstraction.
- A type that is never named or bound, such as the type of an intermediate expression, does not create a dependency.
- Effects are found by heuristics, so some pure code is called effectful, such as a pure `let empty () = ...` factory, and some effectful code is missed, such as a call to an effectful library the tool doesn't know. An effect passed in as a function parameter is invisible by design.
- Effects inside lambdas and local functions count toward the declaration that contains them.
- In C#, LINQ query syntax (`from … select`) and the `Dispose` call behind a pattern-based `using` create no dependency.
- A C# project that references an F# project cannot be analysed yet.
- A C# component's project name is its assembly name, which is normally the same as the project name.

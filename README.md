# fsharp-ballast

A command-line tool that measures Robert C. Martin's package metrics for F# code: **Abstractness**, **Instability** and **Distance from the Main Sequence**. Point it at a project or solution and it reports how each component balances stability against abstraction, and which ones sit in the Zone of Pain or the Zone of Uselessness.

It reads your code through the F# compiler, so it sees the declarations you wrote rather than the extra types the compiler generates.

## Install

```
dotnet tool install --global fsharp-ballast
```

Or as a local tool in a repository:

```
dotnet new tool-manifest
dotnet tool install fsharp-ballast
dotnet fsharp-ballast MySolution.sln
```

It needs the .NET SDK your projects build with, because it evaluates them with MSBuild. It restores them first, but never builds them.

## Usage

```
fsharp-ballast MySolution.slnx
fsharp-ballast src/MyApp/MyApp.fsproj --format html --output metrics.html
fsharp-ballast MySolution.sln --level project --max-distance 0.7
```

```
Component                 Nc  Na  Ca  Ce      A      I      D   Data  Zone
Layered::Fixture.Domain    2   0   1   0  0.000  0.000  1.000  0.500  pain
Layered::Fixture.App       2   0   0   2  0.000  1.000  0.000  0.000
Layered::Fixture.Ports     2   2   2   0  1.000  0.000  0.000  0.000

3 components, mean distance 0.333, 0 isolated
```

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
| Data | Share of counted declarations that are concrete records, unions or enums |
| Zone | `pain` or `uselessness` when D is beyond the distance threshold |

A component is either stable and abstract, so others can depend on it safely, or unstable and concrete, so it is easy to change. The **Zone of Pain** holds concrete components that many others depend on, which are hard to change. The **Zone of Uselessness** holds abstract components nothing depends on.

A stable component full of immutable records often lands in the Zone of Pain without being a real problem, since changing data shapes is its only risk. The Data column shows when that is the case.

A value is shown as `-` when it is undefined. Instability needs at least one dependency in or out, and abstractness needs at least one counted declaration. Undefined distances are left out of the mean.

### What counts as abstract

With the default `fsharp` profile, these count as abstract: interfaces, abstract classes, type abbreviations of function types such as `type Clock = unit -> DateTime`, records whose fields are all functions, and delegates. With `--profile classic`, only interfaces and abstract classes do, which matches object-oriented tools.

### What counts as a dependency

A declaration depends on another when its code names it, or uses one of its members, union cases, fields or active patterns. Types that are only inferred are included too, through the signatures of named functions and values. Unused `open` declarations create no dependency.

Dependencies on the .NET libraries and FSharp.Core never count. Dependencies on NuGet packages count only with `--include-external`.

## Options

| Option | Effect |
| --- | --- |
| `--level project\|namespace\|module` | What counts as a component. Default: `namespace` |
| `--profile fsharp\|classic` | What counts as abstract. Default: `fsharp` |
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

Test projects are recognised by the MSBuild `IsTestProject` property, which `Microsoft.NET.Test.Sdk` sets. A test project without it is analysed unless you exclude it. `--exclude` only hides components from the report: their dependencies still count in other components' numbers.

The HTML report is a single self-contained page with a plot of every component against the main sequence and the full table. It works offline and in light or dark mode.

## Use in CI

```
fsharp-ballast MySolution.sln --max-distance 0.7 --format html --output metrics.html
```

| Exit code | Meaning |
| --- | --- |
| 0 | Success |
| 1 | A component's distance exceeds `--max-distance`. The report is still written, and the components are listed on standard error |
| 2 | Invalid arguments, or the analysis failed: a project could not be loaded or has compile errors |

## Limitations

- One configuration and one target framework are analysed per run, so code in inactive `#if` branches is not seen.
- Dependencies created only through reflection are not seen.
- Signature files (`.fsi`) are not analysed separately and do not count as abstraction.
- A type that is never named or bound, such as the type of an intermediate expression, does not create a dependency.

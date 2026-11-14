# fsharp-ballast

[![NuGet](https://img.shields.io/nuget/v/fsharp-ballast)](https://www.nuget.org/packages/fsharp-ballast)
[![CI](https://github.com/shazmodan/fsharp-ballast/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/shazmodan/fsharp-ballast/actions/workflows/ci.yml)

A command-line tool that measures Robert C. Martin's package metrics for F# and C# code: **Abstractness**, **Instability** and **Distance from the Main Sequence**, plus **effectfulness**. Point it at a project or solution and it reports how each component balances stability against abstraction, and which ones sit in the Zone of Pain or the Zone of Uselessness.

It reads F# through the F# compiler and C# through Roslyn, so it sees the declarations you wrote rather than the extra types the compilers generate. A solution can mix both languages.

## Why the name "ballast"?
Ballast primarily means a heavy material or device used to provide stability, balance, or a steady foundation.
Also in Swedish it means "the coolest" 😎

## Install

Install it as a global tool from NuGet:

```
dotnet tool install --global fsharp-ballast
```

Or as a local tool in a repository:

```
dotnet new tool-manifest
dotnet tool install fsharp-ballast
dotnet fsharp-ballast MySolution.sln
```

The version is below 1.0, so options and report formats may still change between releases.

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

After the summary, the table lists recommendations: general guidance on moving toward the main sequence, then advice for each component beyond the distance threshold, furthest first. The HTML report has the same section. CSV ends each row with an `advice` column, empty for components that need none, and JSON ends with a `recommendations` object holding the guidance and the advice for each component. Pass `--no-recommendations` to leave all of it out.

## Building from source

To try the latest code, or a change of your own, before it is released, clone the repository, pack the tool and install it from the local package. Building needs the .NET 10 SDK.

```
git clone https://github.com/shazmodan/fsharp-ballast.git
cd fsharp-ballast
dotnet pack src/Ballast.Cli -c Release -o ./nupkg
dotnet tool install --global fsharp-ballast --add-source ./nupkg
```

If the tool is already installed, from NuGet or from an earlier pack, `install` refuses. Run `dotnet tool update --global fsharp-ballast --add-source ./nupkg` instead.

NuGet caches a package by its version, so a new pack with the same version can be ignored in favour of the cached one. Raise `<Version>` in `src/Ballast.Cli/Ballast.Cli.fsproj` before packing, for example to `0.1.1-local`. Or uninstall the tool and clear the cache, which makes every project download its packages again on the next restore:

```
dotnet tool uninstall --global fsharp-ballast
dotnet nuget locals global-packages --clear
```

To run it from the clone without installing it:

```
dotnet run --project src/Ballast.Cli -- path/to/MySolution.slnx --format html --output report.html
```

To remove it, run `dotnet tool uninstall --global fsharp-ballast`.

## The metrics

The metrics come from Robert C. Martin's work on package design, in *Agile Software Development: Principles, Patterns, and Practices*. They measure each component, which is a namespace by default, or a project or module with `--level`. Together they show whether a component is easy to change where it needs to be and safe to depend on where it needs to be.

### Instability

Instability, I, measures how hard a component is to change, judged by its dependencies:

```
I = Ce / (Ca + Ce)
```

- **Afferent coupling**, Ca, counts the declarations outside the component that depend on it. These are its incoming dependencies, the code that may break when it changes.
- **Efferent coupling**, Ce, counts the declarations inside the component that depend on something outside it. These are its outgoing dependencies, the reasons it may have to change.

I runs from 0 to 1. A component at 0 is **stable**: others depend on it and it depends on nothing, so a change to it ripples out to all its dependents. That makes it hard to change, but it also has no reason to change because of others. A component at 1 is **unstable**: nothing depends on it and it depends on others, so it is free to change, and changes elsewhere may force it to.

Neither end is bad. Martin's Stable Dependencies Principle says dependencies should point toward stability: a component should only depend on components more stable than itself.

### Abstractness

Abstractness, A, measures how much of a component is contract rather than implementation:

```
A = Na / Nc
```

Nc counts the component's declarations: types, and modules that contain values or functions. Na counts the abstract ones among them. A runs from 0, fully concrete, to 1, fully abstract.

Classic object-oriented tools count interfaces and abstract classes as abstract. In functional code a contract is usually a function type, so under the default `functional` profile delegates, function-type abbreviations, records of functions and free-monad instruction unions count too. [What counts as abstract](#what-counts-as-abstract) has examples.

Martin's Stable Abstractions Principle says a component should be as abstract as it is stable. A stable component should be abstract, so it can be extended without being changed. An unstable component should be concrete, because its implementation is what is meant to change.

### Distance from the Main Sequence

Plot every component with I on the x axis and A on the y axis. The two principles together say components should lie on the line from (0, 1), stable and abstract, to (1, 0), unstable and concrete. Martin calls this line the **main sequence**. A component on it has the balance between abstractness and stability that its dependencies call for.

The distance from the main sequence, D, measures how far a component is from that balance:

```
D = |A + I - 1|
```

D runs from 0, on the line, to 1, in one of the two corners furthest from it. A low mean distance across a code base means its components are generally balanced. A high D on one component points to a design worth looking at, and which side of the line it is on says what kind of problem it is.

## Identifying problematic design patterns

The two corners furthest from the main sequence are where the design problems are. The tool puts a component in a zone when its D is beyond the distance threshold, 0.5 by default, and it lies in that corner.

### The Zone of Pain

The bottom left corner, near (0, 0), holds components that are **stable and concrete**. Much depends on them, but they offer no abstractions, so dependents are bound to their implementation. Any change ripples out to all of them, so the components become rigid and painful to change. A database schema or a concrete data-access class that the whole application uses is the typical example.

Not everything here is a problem. Martin points out that a stable, concrete component that rarely changes, such as `String`, does no harm. In functional code that is the common case: a module of pure functions or immutable records behaves like `String`, because it has no reason to change other than its own logic. What makes stable, concrete code risky is side effects, such as I/O, mutable state or the clock, which tie it to things outside the program that do change.

So under the default `functional` profile, a component is only in the Zone of Pain when its effectfulness, E, is also beyond the effect threshold, 0.25 by default. Under `--profile classic`, effects are ignored and every stable, concrete component far from the main sequence is in the zone, as in Martin's original definition. [What counts as effectful](#what-counts-as-effectful) lists the signals the tool looks for, and [functional-abstractness.md](https://github.com/shazmodan/fsharp-ballast/blob/main/functional-abstractness.md) gives the reasoning.

To get a component out of the Zone of Pain, move its side effects to the edge of the program so the component keeps the pure logic, or give dependents abstractions to use, such as interfaces or function types, instead of its concrete code.

#### Example: an order store in the Zone of Pain

Here `Shop.Orders` holds the order type, the code that reads and writes orders as files, and the workflow that places an order. Billing, shipping and the application all use it, and it uses nothing else in the solution, so it is stable. Everything in it is concrete, and two of its three declarations touch the file system, the clock or a random number generator:

```fsharp
namespace Shop.Orders

open System
open System.IO
open System.Text.Json

type Order =
    { Id: int
      Customer: string
      Total: decimal
      PlacedAt: DateTime }

module OrderStore =
    let private path (id: int) = Path.Combine("orders", $"{id}.json")

    let load (id: int) : Order =
        JsonSerializer.Deserialize<Order>(File.ReadAllText(path id))

    let save (order: Order) : unit =
        File.WriteAllText(path order.Id, JsonSerializer.Serialize order)

module OrderService =
    let place (customer: string) (total: decimal) : Order =
        let order =
            { Id = Random.Shared.Next()
              Customer = customer
              Total = total
              PlacedAt = DateTime.Now }
        OrderStore.save order
        order

namespace Shop.Billing

open Shop.Orders

module Invoice =
    let forOrder (id: int) : string =
        let order = OrderStore.load id
        $"Invoice for {order.Customer}: {order.Total}"

namespace Shop.Shipping

open Shop.Orders

module Label =
    let forOrder (id: int) : string =
        let order = OrderStore.load id
        $"Ship to {order.Customer}, placed {order.PlacedAt:d}"

namespace Shop.App

open Shop.Orders

module Main =
    let run () =
        let order = OrderService.place "Ada" 42m
        printfn "%s" (Shop.Billing.Invoice.forOrder order.Id)
        printfn "%s" (Shop.Shipping.Label.forOrder order.Id)
```

```
Component            Nc  Na  Ca  Ce      A      I      D      E   Data  Zone
Shop::Shop.Orders     3   0   3   0  0.000  0.000  1.000  0.667  0.333  pain
Shop::Shop.Billing    1   0   1   1  0.000  0.500  0.500  0.000  0.000
Shop::Shop.Shipping   1   0   1   1  0.000  0.500  0.500  0.000  0.000
Shop::Shop.App        1   0   0   1  0.000  1.000  0.000  1.000  0.000
```

Billing and shipping are bound to a file layout they never asked for. Moving orders to a database, or testing an invoice without a disk, means changing `Shop.Orders` and everything that calls `OrderStore.load`.

The fix applies both remedies. `Shop.Orders` keeps the data and the pure logic, and states what it needs from storage as a record of functions. Billing and shipping take an `Order` instead of loading one. The file I/O, the clock and the random ID move to `Shop.App`, the edge of the program, which builds the real store and passes values in:

```fsharp
namespace Shop.Orders

open System

type Order =
    { Id: int
      Customer: string
      Total: decimal
      PlacedAt: DateTime }

// The contract: what order code needs from storage, as functions.
type OrderStore =
    { Load: int -> Order
      Save: Order -> unit }

module Order =
    let create (id: int) (placedAt: DateTime) (customer: string) (total: decimal) : Order =
        { Id = id
          Customer = customer
          Total = total
          PlacedAt = placedAt }

namespace Shop.Billing

open Shop.Orders

module Invoice =
    let forOrder (order: Order) : string =
        $"Invoice for {order.Customer}: {order.Total}"

namespace Shop.Shipping

open Shop.Orders

module Label =
    let forOrder (order: Order) : string =
        $"Ship to {order.Customer}, placed {order.PlacedAt:d}"

namespace Shop.App

open System
open System.IO
open System.Text.Json
open Shop.Orders

// The implementation of the contract lives at the edge.
module FileOrderStore =
    let private path (id: int) = Path.Combine("orders", $"{id}.json")

    let store: OrderStore =
        { Load = fun id -> JsonSerializer.Deserialize<Order>(File.ReadAllText(path id))
          Save = fun order -> File.WriteAllText(path order.Id, JsonSerializer.Serialize order) }

module Main =
    let run () =
        let store = FileOrderStore.store
        let order = Order.create (Random.Shared.Next()) DateTime.Now "Ada" 42m
        store.Save order
        printfn "%s" (Shop.Billing.Invoice.forOrder (store.Load order.Id))
        printfn "%s" (Shop.Shipping.Label.forOrder (store.Load order.Id))
```

```
Component            Nc  Na  Ca  Ce      A      I      D      E   Data  Zone
Shop::Shop.Orders     3   1   4   0  0.333  0.000  0.667  0.000  0.333
Shop::Shop.Billing    1   0   1   1  0.000  0.500  0.500  0.000  0.000
Shop::Shop.Shipping   1   0   1   1  0.000  0.500  0.500  0.000  0.000
Shop::Shop.App        2   0   0   2  0.000  1.000  0.000  1.000  0.000
```

`Shop.Orders` is still stable, and even more depended on, but it is no longer in the Zone of Pain. The `OrderStore` record raises its abstractness, and with the effects gone its effectfulness drops to 0, so what is left behaves like `String`: it only changes when the order rules do. A distance of 0.667 is acceptable for pure code like this. The effects now sit in `Shop.App`, which is unstable and concrete, on the main sequence, where change is cheap because nothing depends on it. Swapping the file store for a database, or for an in-memory store in a test, is a new `OrderStore` value and touches neither billing nor shipping.

Under `--profile classic` the record of functions does not count as abstract and effects are ignored, so `Shop.Orders` stays in the zone there. To satisfy that profile too, the contract would have to be an interface.

### The Zone of Uselessness

The top right corner, near (1, 1), holds components that are **abstract and unstable**. They declare interfaces or other abstractions, but nothing depends on them, while they depend on others. Abstractions nobody uses add indirection without benefit. They are often left over from a design that changed, or written for flexibility that never came.

To get a component out of the Zone of Uselessness, remove the abstractions nothing implements or uses, merge the rest into the component that uses them, or, if they are meant to be shared, make them depend on less so that they become stable.

#### Example: notification contracts in the Zone of Uselessness

Here `Shop.Notifications` was written ahead of need, for a time when the shop might send texts as well as emails, format messages in several ways and retry failed sends. It holds three contracts and no implementation. It depends on `Shop.Orders`, and the only thing that depends on it is the one email sender in `Shop.App`:

```fsharp
namespace Shop.Orders

type Order =
    { Id: int
      Customer: string
      Total: decimal }

namespace Shop.Notifications

open System
open Shop.Orders

type Notifier = { Send: Order -> unit }

type MessageFormatter = Order -> string

type RetryPolicy = int -> TimeSpan option

namespace Shop.App

open Shop.Orders
open Shop.Notifications

module Email =
    let notifier: Notifier =
        { Send = fun order -> printfn "To %s: thanks for order %d" order.Customer order.Id }

    let confirm (order: Order) : unit = notifier.Send order

module Main =
    let run () =
        Email.confirm { Id = 1; Customer = "Ada"; Total = 42m }
```

```
Component                 Nc  Na  Ca  Ce      A      I      D      E   Data  Zone
Shop::Shop.Orders          1   0   4   0  0.000  0.000  1.000  0.000  1.000
Shop::Shop.Notifications   3   3   1   2  1.000  0.667  0.667  0.000  0.000  uselessness
Shop::Shop.App             2   0   0   2  0.000  1.000  0.000  1.000  0.000
```

`Shop.Notifications` is fully abstract, but nothing needs it to be. Nothing implements `MessageFormatter` or `RetryPolicy`, and `Notifier` has a single implementation and a single user. Every reader still has to follow the indirection, and a change to `Order` can still ripple through contracts that do nothing.

The fix applies the first two remedies. The two contracts nothing uses are deleted, and `Notifier`, which is worth keeping so a test can send notifications to a list instead of to customers, moves into `Shop.App`, next to the code that implements and uses it:

```fsharp
namespace Shop.Orders

type Order =
    { Id: int
      Customer: string
      Total: decimal }

namespace Shop.App

open Shop.Orders

// The one contract that is used, next to its implementation and its user.
type Notifier = { Send: Order -> unit }

module Email =
    let notifier: Notifier =
        { Send = fun order -> printfn "To %s: thanks for order %d" order.Customer order.Id }

    let confirm (order: Order) : unit = notifier.Send order

module Main =
    let run () =
        Email.confirm { Id = 1; Customer = "Ada"; Total = 42m }
```

```
Component          Nc  Na  Ca  Ce      A      I      D      E   Data  Zone
Shop::Shop.Orders   1   0   3   0  0.000  0.000  1.000  0.000  1.000
Shop::Shop.App      3   1   0   3  0.333  1.000  0.333  0.667  0.000
```

The component is gone, and so is the zone. `Shop.App` is unstable and mostly concrete, close to the main sequence, and its one abstraction is there because it is used. `Shop.Orders` is far from the main sequence but pure data, so it is not in the Zone of Pain, as the previous example explains. If a second application later needs to send notifications, that is the time to move `Notifier` into a shared component, and to keep that component free of other dependencies so it is stable.

Under `--profile classic`, records of functions and function types are not abstract, so `Shop.Notifications` is not flagged at all there. The functional profile is what makes unused function contracts visible.

The HTML report lists every component beyond the distance threshold, with advice for its position.

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
| Zone | `pain` or `uselessness` when D is beyond the distance threshold, as described in [Identifying problematic design patterns](#identifying-problematic-design-patterns) |

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

#### C# examples

As with F#, each comment gives the classification under `functional`, then under `classic`, and every example was checked by running the tool on it.

Abstract under both profiles:

```csharp
// 1. An interface. Abstract / abstract.
public interface IUserStore
{
    string? Find(int id);
    void Save(int id, string name);
}

// 2. An interface with a default implementation. Still abstract / abstract.
public interface IGreeter
{
    string Name { get; }
    string Greet() => $"Hello {Name}";
}

// 3. An abstract class, even with some implemented members. Abstract / abstract.
public abstract class Shape
{
    public abstract double Area { get; }
    public string Describe() => $"Area {Area}";
}

// 4. An abstract record. Abstract / abstract.
public abstract record Event(DateTime At);
```

Abstract only under `functional`, because a function type is the functional way to state a contract:

```csharp
// 5. A delegate. Abstract / concrete.
public delegate bool Validator(string value);

// 6. A record whose positional parameters are all delegates, a record of ports.
//    Abstract / concrete.
public record UserPorts(Func<int, string?> Find, Action<int, string> Save);
```

Concrete under both profiles:

```csharp
// 7. A record of data. Concrete, counted as data.
public record User(int Id, string Name);

// 8. A record struct. Concrete, counted as data.
public readonly record struct Point(int X, int Y);

// 9. An enum. Concrete, counted as data.
public enum Level { Low, High }

// 10. A class that implements an abstraction. Concrete.
public sealed class Circle(double radius) : Shape
{
    public override double Area => Math.PI * radius * radius;
}

// 11. A static class with methods, the C# counterpart of a module. Concrete.
public static class Pricing
{
    public static decimal Discount(decimal rate, decimal price) => price * (1 - rate);
}

// 12. A class with mutable state. Concrete, and effectful because of its
//     non-readonly field and void method.
public class Counter
{
    private int count;
    public void Increment() => count++;
    public int Count => count;
}

// 13. A plain struct. Concrete, but not counted as data, because only records
//     and enums are. Init-only properties are not mutable state.
public struct Size
{
    public int Width { get; init; }
    public int Height { get; init; }
}

// 14. A record that mixes data and delegates. One plain parameter makes it
//     concrete, counted as data.
public record Service(string Name, Action Run);

// 15. A record whose delegates are properties in its body rather than positional
//     parameters. Only positional parameters are looked at, so it is concrete,
//     counted as data.
public record Handlers
{
    public required Func<int, string> Load { get; init; }
}
```

Not counted under either profile, so they change neither Nc nor A:

```csharp
// 16. A static class that only holds types. The static class is not counted,
//     but OrderId is counted on its own as a concrete data type.
public static class Types
{
    public record OrderId(int Value);
}

// 17. A using alias. It declares no type, so there is nothing to count. That
//     holds for an alias of a delegate type such as Func<DateTime> too, unlike
//     F#'s type Clock = unit -> DateTime.
using UserId = int;
```

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
| `--no-recommendations` | Leave the recommendations out of the report: the section in `table` and `html`, the `advice` column in `csv` and the `recommendations` object in `json` |

Test projects are recognised by the MSBuild `IsTestProject` property, which `Microsoft.NET.Test.Sdk` sets. A test project without it is analysed unless you exclude it. `--exclude` only hides components from the report: their dependencies still count in other components' numbers.

The HTML report is a single self-contained page with a plot of every component against the main sequence, recommendations for each component beyond the distance threshold, and the full table. It works offline and in light or dark mode.

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

## License

MIT. See [LICENSE](https://github.com/shazmodan/fsharp-ballast/blob/main/LICENSE).

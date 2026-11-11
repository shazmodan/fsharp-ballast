# Plan: functional abstractness and effectfulness

Bring the tool in line with `functional-abstractness.md`. That file argues that in functional code, the risky combination is not just stable and concrete, but stable, concrete and **effectful**. It proposes:

```
A = abstract / (abstract + concrete)        -- excluded items left out
E = effectful concrete / (abstract + concrete)
```

A component is a problem when it is stable (low I), has low A and has high E.

This plan comes before milestones 16 and 17 of `csharp-plan.md`. Its milestones are numbered F1 to F5 to keep them apart.

## 1. Where the tool already stands

The `functional` profile already classifies every construct in section 2.1 of `functional-abstractness.md` as that file does, for F# and, by mirroring, for C#. Interfaces with static abstract members are abstract, as interfaces. Object expressions and `.fsi` files count for nothing.

Four things are missing:

1. **Effectfulness.** Nothing tags a concrete declaration as pure or effectful, so E cannot be computed.
2. **Free-monad instruction unions,** which are concrete today and should be abstract.
3. **Function-level abstraction,** such as SRTP inline functions and higher-order functions as "partly abstract". The tool counts declarations, meaning types and modules, not functions.
4. **Type providers as volatile dependencies.**

This plan covers 1 and 2. Items 3 and 4 are left as decisions in section 7.

## 2. Design decisions

| Topic | Decision | Reason |
| --- | --- | --- |
| What E is measured on | Concrete declarations only. Abstract declarations are never effectful | An abstraction such as `type Clock = unit -> DateTime` is how an effect gets pushed behind a boundary. That is the point of a port. |
| How a declaration becomes effectful | It has at least one **effect signal** (section 4) in its own code | Signals can be listed and explained. A single bit per declaration keeps E a simple share, like A. |
| Signals are kept, not just the bit | `Declaration.EffectSignals` lists every signal found, with the member it came from | The heuristics will produce false positives, and users need to see why a module was called effectful. |
| Effect propagation | Direct only: calling an effectful module does not make the caller effectful | Propagation along dependencies would mark almost everything that touches the shell as effectful, which hides where the effects actually are. Section 7 asks you to confirm this. |
| Profiles | E is computed under both profiles. Only the zone rule (section 5) depends on the profile | E is useful on its own, and `classic` stays comparable with OO tools. |
| Effectful APIs | A built-in list of namespaces, types and members (section 4.3), which can later be extended from the command line | "References I/O, the database or `DateTime.Now`" needs a concrete list, and the list is where most of the judgement lies. |

## 3. Model changes

```fsharp
type EffectSignal =
    | ReturnsUnit of memberName: string         // F# unit, C# void
    | ReturnsAsync of memberName: string        // Async<_>, Task, Task<_>, ValueTask, ValueTask<_>
    | TakesUnit of memberName: string           // F# only: a unit parameter, as in `let now () = ...`
    | MutableState of memberName: string        // mutable binding or field, settable property
    | UsesEffectfulApi of memberName: string * api: string

type Declaration =
    { ...existing fields...
      EffectSignals: EffectSignal list }        // empty for abstract and not-counted declarations

type ComponentMetrics =
    { ...existing fields...
      E: float option }                         // undefined when Nc is 0
```

`E = (concrete declarations with at least one signal) / Nc`, where `Nc` already means abstract plus concrete under the selected profile.

## 4. Effect signals

A signal is looked for in the members a declaration defines itself: functions and values in a module, and members, fields and properties of a type. Constructors, compiler-generated members, and the members records and unions get automatically (equality, comparison, `ToString`, deconstruction) are ignored.

### 4.1 F#

| Signal | Detection (FCS) | Known false positives |
| --- | --- | --- |
| `ReturnsUnit` | A function or method whose final return type is `unit`. A property setter counts as `MutableState` instead | A pure function that returns `unit` is rare, so few |
| `ReturnsAsync` | Final return type `Async<_>`, `Task`, `Task<_>`, `ValueTask` or `ValueTask<_>` | An async function that only computes |
| `TakesUnit` | A curried parameter group that is exactly `unit`, such as `let now () = ...` | Pure factories, such as `let empty () = { Items = [] }`. Section 7 asks whether to keep this signal |
| `MutableState` | A `let mutable` value in a module or class, a `mutable` record field, or a field of type `ref` | A local `mutable` inside a function body is not state and does not count |
| `UsesEffectfulApi` | A symbol use inside the declaration whose target matches the list in section 4.3 | A deliberate pure use, such as reading `DateTime.MinValue`, is avoided by listing members rather than whole types where needed |

Pure data stays pure: a record, union or enum with no mutable fields and no members of its own has no signals.

### 4.2 C#

| Signal | Detection (Roslyn) |
| --- | --- |
| `ReturnsUnit` | A method that returns `void`, or a property with a `set` accessor, which `init` accessors don't count as |
| `ReturnsAsync` | A method returning `Task`, `Task<T>`, `ValueTask` or `ValueTask<T>` |
| `MutableState` | A field that is not `readonly` or `const`, or an auto-property with a `set` accessor |
| `UsesEffectfulApi` | A symbol use inside the type that matches the list in section 4.3 |

C# has no `unit` parameter, so `TakesUnit` does not apply.

### 4.3 Effectful APIs

Matched on the target symbol's namespace, type or member:

- **Namespaces:** `System.IO`, `System.Net`, `System.Data`, `Microsoft.Data`, `Microsoft.EntityFrameworkCore`, `System.Diagnostics.Process`
- **Types:** `System.Console`, `System.Random`, `System.Environment`, `System.Threading.Thread`, `System.Diagnostics.Stopwatch`, `System.Net.Http.HttpClient`
- **Members:** `DateTime.Now`, `DateTime.UtcNow`, `DateTime.Today`, `DateTimeOffset.Now`, `DateTimeOffset.UtcNow`, `Guid.NewGuid`, `Random.Shared`

A listed type counts only through a member use, since naming the type performs nothing, and `Environment.NewLine` is treated as pure (found on Argu, 2026-11-11). Attribute classes report no mutable state, because their settable properties are named arguments (found on GuardClauses, 2026-11-11).

These are framework APIs, which the extractor currently drops as dependencies. Signal detection therefore runs on symbol uses before the framework filter.

## 5. Zones under the functional profile

Today a component is in the Zone of Pain when `A < 0.5`, `I < 0.5` and `D` is beyond the distance threshold. Under the `functional` profile, the Zone of Pain additionally requires **`E` beyond an effect threshold**, `--effect-threshold`, default `0.25`. A stable, concrete, pure component is then reported without a zone, as `functional-abstractness.md` calls it healthy.

The Zone of Uselessness is unchanged. Under `classic`, both zones are unchanged. When `E` is undefined, the functional Zone of Pain does not apply.

`--max-distance` keeps gating on `D` alone. A new `--fail-on-zone pain` option would let CI fail on the functional Zone of Pain instead (section 7).

## 6. Free-monad instruction unions

Under the `functional` profile, a union counts as abstract when it describes operations rather than data:

- it has at least one type parameter `'next`
- the last field of every case is `'next`, or a function whose final return type is `'next`
- at least one case has such a function

For example, `type Instruction<'next> = | ReadUser of UserId * (User -> 'next) | WriteUser of User * 'next`. Under `classic` it stays concrete.

The wrapper type, such as `Program<'a> = Free of Instruction<Program<'a>> | Pure of 'a`, does not match and stays concrete, which is correct because it is plumbing.

## 7. Decisions

Decided on 2026-11-10:

1. **Zone rule.** Under the `functional` profile, the Zone of Pain needs `E` beyond `--effect-threshold`.
2. **Propagation.** Effects are direct only. Depending on an effectful declaration does not make a declaration effectful.
3. **`TakesUnit`.** Kept, as `functional-abstractness.md` lists it, accepting false positives from pure factories.
4. **Function-level abstraction.** Left out for now. `Nc` and `Na` keep counting declarations.
5. **`--fail-on-zone`.** Added in milestone F5.
6. **Type providers.** Left out for now.

## 8. Milestones

Each one test-first at seams agreed before the tests, as before.

**F1. Model and metric. Done 2026-11-10.** `Declaration.EffectSignals`, `ComponentMetrics.E` and `Metrics.Options.EffectThreshold` are in place. Under the functional profile, the Zone of Pain needs `E` beyond the threshold, fixed at 0.25 until `--effect-threshold` arrives in F5. Tested with pure `Metrics.calculate` cases for E, undefined E, both profiles' zone rules, the threshold boundary and the unchanged Zone of Uselessness, plus an `Extraction.merge` case carrying signals. Extractors still produce no signals, so until F2 and F3 the default functional profile reports no Zone of Pain. Eight CLI tests that check the classic zone now pass `--profile classic`, which gives the same numbers on their fixtures.

**F2. F# effect signals. Done 2026-11-10.** The F# extractor attaches signals to each concrete declaration. A member signal names its member. An effectful-API signal names its declaration, because uses are attributed to declarations, not members. Interfaces and abstract classes get no signals. The API list lives in `EffectfulApis.fs` for both languages, adds FSharp.Core's `printf` family and `stdin`, `stdout` and `stderr`, and treats `System.IO.Path` as pure. Tested on an `FSharpEffects` fixture: all 16 effectful cases failed before the change, and the 8 pure cases stayed pure. Original acceptance: an `FSharpEffects` fixture with one declaration per row of section 4.1 and per entry kind in section 4.3, plus cases that must stay pure: pure modules, pure data, a local `mutable` inside a function, an abstract port, and a record's generated members.

**F3. C# effect signals. Done 2026-11-10.** The C# extractor attaches the signals in section 4.2 and uses the shared API list. Members the compiler synthesises, such as a positional record's `Deconstruct` and backing fields, are skipped through `IsImplicitlyDeclared`. Interfaces and abstract classes get no signals. Tested on a `CSharpEffects` fixture: all 12 effectful cases failed before the change, and the 9 pure cases stayed pure, including a positional record, a readonly struct, an enum and a delegate. Original acceptance: a `CSharpEffects` fixture with one type per row of section 4.2 and per entry kind in section 4.3, plus pure cases: a static class of pure functions, a record with `init` properties, `readonly` and `const` fields, and an interface.

**F4. Free-monad unions. Done 2026-11-10.** The F# classifier treats an instruction union as abstract under `functional` and concrete under `classic`, by the rule in section 6. Tested in the classification fixture with an instruction union, which failed before the change, its `Program` wrapper, and three near misses: a generic data union, a union with an empty case, and a union whose cases all end in `'next` but has no function. Original acceptance: fixture unions for the instruction pattern, a wrapper type and near misses: a generic union that holds data, and one with a case whose last field is not `'next`. Each is checked under both profiles.

**F5. CLI and reports. Done 2026-11-10.** E is in all four formats, with `e` in JSON and CSV, an E column in the table and HTML table, and E in the HTML tooltip. `--effect-threshold` and the repeatable `--fail-on-zone pain|uselessness` are added, and exit code 1 now also covers a component in a named zone. The `EffectZones` fixture reproduces its hand-calculated table: a stable pure component with no zone and a stable effectful one in the Zone of Pain, plus classic-profile, threshold and gating checks. The README explains E, its signals and the new options, and its example output comes from a real run. The CSV header gained `e`, so the CSV test's expected rows changed. Original plan: an `e` value in JSON and CSV, an `E` column in the table and HTML table, `E` in the HTML tooltip, and `--effect-threshold`. Add `--fail-on-zone` if chosen. An end-to-end fixture reproduces a hand-calculated table in which a stable pure component has no zone and a stable effectful one is in the Zone of Pain. The README explains E and the signals.

After F5, return to milestones 16 and 17 of `csharp-plan.md`. Milestone 16's README work then covers E as well.

## 9. Limitations to state in the README

- Effects are found by heuristics, not tracked by the compiler, so some pure code will be called effectful and some effectful code will be missed. An effect hidden behind a function parameter is invisible by design.
- Effects inside lambdas and local functions count toward the declaration that contains them.
- Signals come from a declaration's own code only, unless propagation is chosen in section 7.

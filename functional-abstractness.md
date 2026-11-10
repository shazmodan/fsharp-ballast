# Abstractness, Instability and Distance from the Main Sequence in Functional Languages

## 1. Should FP and OO interpret Abstract vs Concrete differently?

Yes, I think the interpretation should differ, even though the underlying principle stays the same. Martin's formula **A = Nₐ / N꜀** (abstract classes and interfaces divided by all classes) is a proxy, and it was tuned for OO. What he actually cares about is whether others can depend on a module without being dragged along when its implementation changes. Functional languages answer that question with different mechanisms, and in some cases they change the answer.

### 1.1 What counts as "abstract" needs translating

FP has no abstract classes, but it has clear equivalents:

| OO abstraction | FP equivalent |
|---|---|
| Interface / abstract class | Type class (Haskell), protocol (Clojure), trait (Scala/Rust), module signature / functor (OCaml/F#) |
| Strategy pattern, injected dependency | Higher-order function parameter |
| Generic base class | Parametric polymorphism (`map :: (a -> b) -> [a] -> [b]`) |
| Repository interface | Effect interface (tagless final, algebraic effects, free monads) |

A fair FP version of A might be the share of a module's exported surface that is parameterized over behavior or type, not the share of declarations that are interfaces. A function that takes a function as an argument is abstract in exactly the dependency-inversion sense: the caller supplies the concrete part.

### 1.2 Purity makes the Zone of Pain hurt less

The Zone of Pain (stable and concrete) is bad in OO because concrete classes tend to carry state, side effects and hidden coupling, so changing them ripples outward. Martin himself made an exception for non-volatile things like `String`.

In FP, a large share of code is pure, total, well-typed functions, and those behave like `String`. Modules like `Data.List` or `List` in F# are maximally concrete and maximally stable, and that's fine.

So in FP the axis that matters may be less **abstract vs concrete** and more **pure vs effectful**:

- A stable, concrete, **pure** module is low risk.
- A stable, concrete module that **talks to a database** is still a real problem.

"Functional core, imperative shell" maps neatly onto this: the core can sit near the Zone of Pain safely, and the shell should be unstable.

### 1.3 The expression problem flips what "open for extension" means

The idea behind "stable modules should be abstract" is that stable things should be extensible without modification. But extensible in which direction?

- **OO:** an interface makes it easy to **add new variants** (a new implementing class) and hard to add new operations, because every implementor must change.
- **FP:** a closed sum type is the reverse: it's easy to **add new operations** (just write another function that pattern-matches) and hard to add new cases, because every match breaks.

A closed algebraic data type looks "concrete" by Martin's count, but it is very open to the kind of extension FP code usually needs. If the domain mostly grows by gaining new behavior over a fixed set of shapes, a stable concrete ADT is the right design, not a smell.

### 1.4 Practical consequence, especially in .NET

Tools that compute these metrics, NDepend for example, count interfaces and abstract types. A C# codebase written in a functional style, with static pure functions, `Func<>`/delegate parameters, records and pattern matching, will look concrete everywhere and get flagged as sitting in the Zone of Pain even when its design is sound. F# modules have the same problem.

I'd treat **D** (distance from the main sequence) as unreliable in that setting unless you redefine A along the lines above. Even then, don't read it as a target; use it to find modules that are both effectful and heavily depended on.

### 1.5 Summary

- The **Stable Dependencies Principle** (depend in the direction of stability) carries over unchanged.
- **Instability**, `I = Ce / (Ca + Ce)`, is language-neutral.
- The **Stable Abstractions Principle** needs reinterpreting. For FP I would rephrase it as:

> **Stable modules should be pure, or abstract over their effects and behavior.**

Concreteness is mainly dangerous when it comes with side effects and volatility, and FP separates those from concreteness much more cleanly than typical OO code does.

---

## 2. Classifying F# constructs

I'd use three buckets instead of two:

- **Abstract** covers anything that names a contract without choosing an implementation.
- **Concrete** covers anything that commits to a representation or an implementation. I'd tag these **pure** or **effectful**, because the Zone of Pain only really hurts on the effectful side.
- **Excluded** covers constructs that are erased at compile time or only organize code. They are neither abstractions nor implementations, so counting them as concrete would just pad the denominator.

### 2.1 The listed constructs

| Construct | Classification | Reasoning |
|---|---|---|
| Interface | **Abstract** | Textbook case. |
| Abstract class | **Abstract** | Same as in Martin's metric, though often a partial abstraction because it can carry concrete members. |
| `type Clock = unit -> DateTime` | **Abstract** | This is a port: a named contract that callers can supply an implementation for. `unit -> X` almost always marks an effect, so this is the ideal kind of abstraction: an effect pushed behind a boundary. |
| `type Id = int` | **Excluded** | An alias with no abstraction and no behavior. It's erased at compile time, and `Id` and `int` are interchangeable. |
| Record with only function fields | **Abstract** | An interface written as a record (a capability record). Semantically the same as an interface. |
| Delegate | **Abstract** | A named function type, like the function abbreviation but a real nominal type. |
| Record / union / enum / struct / exception | **Concrete (pure)** | They commit to a data shape but have no behavior. A union is closed to new cases and open to new functions, so a stable union is fine when the domain grows by new behavior and not by new variants. |
| Class | **Concrete**, pure or effectful | Look at what it does. Mutable state or I/O makes it effectful. |
| Module with values/functions | **Concrete**, pure or effectful | Classify by purity. A stable, pure module is fine. A stable, effectful one is the real Zone of Pain. |
| Module with only nested declarations | **Excluded** | It works like a namespace. Count its contents individually. |
| Unit of measure | **Excluded** | Erased at compile time and only a type-level annotation. Like `type Id = int`, but with more compile-time safety. |

### 2.2 Additional constructs

| Construct | Classification | Reasoning |
|---|---|---|
| Instruction union for a free monad / interpreter (`ReadUser of UserId * (User -> 'next)`) | **Abstract** | Syntactically a concrete union, semantically an effect interface: it describes operations and leaves the interpreter to decide what they do. This is the clearest case where looking at the syntax alone gives the wrong answer. |
| Static abstract members in interfaces (IWSAM, F# 7+) | **Abstract** | The closest F# gets to type classes. |
| Inline functions with SRTP member constraints | **Abstract** (mostly) | Structural abstraction over any type that has the required members. The function body is concrete, but callers depend only on the constraint. |
| Higher-order or generic functions in a module | **Partly abstract** | If you count at function level instead of module level, a function like `retry : (unit -> Async<'a>) -> Async<'a>` is parameterized over behavior. Consider weighting these, or counting per exported function. |
| Active patterns | **Concrete (pure)**, worth noting | These are views: callers match on the pattern and not on the underlying data shape, so they soften the "can't add union cases" problem. Concrete, but they reduce coupling to concrete data. |
| Computation expression builders | **Concrete**, often effectful | They define effect semantics. They sit close to the shell. |
| Object expressions | **Not a type, but an implementation** | Anonymous implementations of interfaces. They don't count toward N꜀, but they show where the concrete part was supplied. |
| Signature files (`.fsi`) | **Not abstract** | They are information hiding, not dependency inversion. Callers still bind to exactly one implementation. They do make a module's surface smaller and more stable, which helps Instability more than Abstractness. |
| `[<Literal>]` / constant values | **Concrete (pure)** | Maximally stable and harmless. |
| Type providers | **Concrete, effectful at compile time** | They couple you to an external schema. In practice they often behave like volatile dependencies. |

### 2.3 Practical consequences

**IL-based tools get this wrong.** Abbreviations and units of measure disappear in compilation. A record of functions compiles to a concrete class with `FSharpFunc` properties. A free-monad union compiles to a class hierarchy. A tool like NDepend that reads assemblies will see almost nothing as abstract. To measure this properly you'd have to work at the source level, for example with FSharp.Compiler.Service, which gives you the typed syntax tree.

**Purity is the hard part to automate.** F# doesn't track effects, so you'd need heuristics, for example:

- signatures that return `unit`, `Async<_>` or `Task<_>`
- parameters of type `unit`
- `mutable`
- references to I/O, the database or `DateTime.Now`

It won't be exact, but it's probably good enough to flag modules that are both stable and effectful. That's the question the metric is really trying to answer.

### 2.4 Proposed metric

```
A = abstract / (abstract + concrete)        -- excluded items left out
E = effectful concrete / (abstract + concrete)
```

A module is a problem when it is **stable, has low A and has a high E**. Stable, concrete and pure is a healthy combination in F#.
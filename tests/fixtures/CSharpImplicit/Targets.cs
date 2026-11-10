using System.Collections;
using System.Runtime.CompilerServices;

namespace Fixture.ImplicitTargets;

public readonly struct Money
{
    public Money(int cents) => Cents = cents;

    public int Cents { get; }

    public static Money operator +(Money left, Money right) => new(left.Cents + right.Cents);

    public static Money operator -(Money value) => new(-value.Cents);

    public static implicit operator Money(int cents) => new(cents);
}

public class Bag
{
    public BagEnumerator GetEnumerator() => new();
}

public class BagEnumerator
{
    public int Current => 0;

    public bool MoveNext() => false;
}

public class Job
{
    public JobAwaiter GetAwaiter() => new();
}

public class JobAwaiter : INotifyCompletion
{
    public bool IsCompleted => true;

    public int GetResult() => 1;

    public void OnCompleted(Action continuation) => continuation();
}

public class Pair
{
    public void Deconstruct(out int first, out int second)
    {
        first = 1;
        second = 2;
    }
}

public class Basket : IEnumerable
{
    public IEnumerator GetEnumerator() => Array.Empty<object>().GetEnumerator();
}

public static class BasketExtensions
{
    public static void Add(this Basket basket, string item) { }
}

/// Hands out instances, so the sources never name the target types.
public static class Factory
{
    public static Money MakeMoney() => new(1);

    public static void TakeMoney(Money money) { }

    public static Bag MakeBag() => new();

    public static Job MakeJob() => new();

    public static Pair MakePair() => new();
}

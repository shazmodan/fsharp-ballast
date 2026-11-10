namespace Fixture.Targets;

public class MarkerAttribute : Attribute { }

public class Order
{
    public int Id { get; init; }
}

public interface IService
{
    int Run();
}

public class Service : IService
{
    public int Run() => 1;
}

public class Base { }

public enum Color { Red, Green }

public delegate int Handler(int value);

public record Point(int X, int Y);

public class Box<T>
{
    public T? Item { get; set; }
}

public static class Functions
{
    public const int Constant = 1;

    public static int Helper(int x) => x + 1;

    public static Order MakeOrder() => new() { Id = 1 };

    public static void Apply(Action<Order> action) => action(MakeOrder());
}

public static class Extensions
{
    public static string Shout(this string text) => text.ToUpperInvariant();
}

public class Outer
{
    public class Inner { }
}

public static class Holder
{
    public class Held { }
}

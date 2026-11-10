namespace Fixture.CSharpEffects;

// ---- Effectful: one type per signal kind ----

public class Logger
{
    public void Write(string text) { }
}

public class Fetcher
{
    public Task<int> FetchAsync() => Task.FromResult(1);
}

public class ValueFetcher
{
    public ValueTask<int> Fetch() => new(1);
}

public class Counter
{
    private int count;

    public int Count => count;
}

public class Settable
{
    public string Name { get; set; } = "";
}

public static class StaticState
{
    public static int Hits;
}

public record MutableRecord
{
    public int Value { get; set; }
}

public class Files
{
    public string Read(string path) => File.ReadAllText(path);
}

public class Times
{
    public DateTime Later(int days) => DateTime.Now.AddDays(days);
}

public class Ids
{
    public Guid Next() => Guid.NewGuid();
}

public class Terminal
{
    public string? Read() => Console.ReadLine();
}

// ---- Pure: must have no signals ----

public static class PureFunctions
{
    public static int Add(int a, int b) => a + b;

    public static string Combine(string folder) => Path.Combine(folder, "file.txt");
}

public record Data(int Id, string Name);

public record InitOnly
{
    public int Id { get; init; }
}

public class WithReadonly
{
    private readonly int seed = 1;
    private const int Max = 3;

    public int Value => seed + Max;
}

public readonly struct Point
{
    public Point(int x) => X = x;

    public int X { get; }
}

public enum Level { Low, High }

public delegate int Handler(int value);

public interface IPort
{
    void Save(int value);
}

public abstract class AbstractBase
{
    public abstract void Run();
}

namespace Fixture.CSharpClassification;

public interface IPort
{
    int Run();
}

public abstract class AbstractBase
{
    public abstract int Run();
}

public class WithVirtual
{
    public virtual int Run() => 1;
}

public delegate int Callback(int value);

public record FunctionRecord(Func<int, string> Load, Action<string> Save);

public record CallbackRecord(Callback Run);

public record MixedRecord(string Name, Func<int, int> Apply);

public record PlainRecord(int Id);

public record EmptyRecord;

public record BodyRecord
{
    public Func<int>? Get { get; init; }
}

public abstract record AbstractRecord;

public record struct StructRecord(int X);

public enum Level { Low, High }

public static class WithMembers
{
    public static int Value => 1;
}

public static class OnlyTypes
{
    public class Nested { }
}

public class Service
{
    public int Run() => 1;
}

public struct Coordinate
{
    public int X;
}

public class FailedException : Exception { }

public class MarkerAttribute : Attribute { }

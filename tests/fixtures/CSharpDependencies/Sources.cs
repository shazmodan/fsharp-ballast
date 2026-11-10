using Fixture.Generated;
using Fixture.Targets;
using static Fixture.Targets.Functions;
using OrderAlias = Fixture.Targets.Order;

namespace Fixture.Sources;

public class UsesTypeInSignature
{
    public void Run(Order order) { }
}

public class UsesBaseType : Base { }

public class UsesInterface : IService
{
    public int Run() => 0;
}

public class UsesGenericArgument
{
    public Box<Order>? Field;
}

public class UsesTypeof
{
    public Type Get() => typeof(Order);
}

public class UsesNameof
{
    public string Get() => nameof(Order);
}

public class UsesCast
{
    public void Run(object value) { _ = (Order)value; }
}

public class UsesPattern
{
    public bool Run(object value) => value is Order;
}

[Marker]
public class UsesAttribute { }

public class UsesMember
{
    public int Run() => Functions.Helper(1);
}

public class UsesMemberOfReturnedObject
{
    public int Run() => Functions.MakeOrder().Id;
}

public class UsesObjectCreation
{
    public object Run() => new Service();
}

public class UsesExtension
{
    public string Run() => "abc".Shout();
}

public class UsesEnumMember
{
    public int Run() => (int)Color.Green;
}

public class UsesConstant
{
    public int Run() => Functions.Constant;
}

public class UsesStaticImport
{
    public int Run() => Helper(2);
}

public class UsesAlias
{
    public OrderAlias? Field;
}

public class UsesVar
{
    public void Run() { var order = Functions.MakeOrder(); }
}

public class UsesLambdaParameter
{
    public void Run() => Functions.Apply(item => { });
}

public class UsesDeconstruction
{
    public int Run()
    {
        var (x, y) = new Point(1, 2);
        return x + y;
    }
}

public class UsesNestedType
{
    public Outer.Inner? Field;
}

public class UsesTypeInStaticClass
{
    public Holder.Held? Field;
}

public class UsesDelegate
{
    public Handler? Field;
}

public class UsesGenerated
{
    public GeneratedThing? Field;
}

public partial class Split
{
    public Order? First;
}

public static class StaticSource
{
    public static int Run() => Functions.Helper(3);
}

public class OnlyGeneric<T>
{
    public T? Item;
}

public class OnlyBcl
{
    public int Run() => DateTime.Now.Year + 1;
}

public class OnlyLocalsAndDynamic
{
    public int Run()
    {
        dynamic anything = 1;
        int local = 2;
        return local;
    }
}

/// <summary>Mentions <see cref="Order"/> only in a comment.</summary>
public class OnlyDocComment { }

public class Recursive
{
    public Recursive? Next;
}

file class Hidden
{
    public Order? Field;
}

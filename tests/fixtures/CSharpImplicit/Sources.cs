using Fixture.ImplicitTargets;

namespace Fixture.ImplicitSources;

public class UsesImplicitConversion
{
    public void Run() => Factory.TakeMoney(5);
}

public class UsesBinaryOperator
{
    public object Run() => Factory.MakeMoney() + Factory.MakeMoney();
}

public class UsesUnaryOperator
{
    public object Run() => -Factory.MakeMoney();
}

public class UsesForeach
{
    public int Run()
    {
        var total = 0;
        foreach (var item in Factory.MakeBag())
        {
            total += item;
        }

        return total;
    }
}

public class UsesAwait
{
    public async Task<int> Run() => await Factory.MakeJob();
}

public class UsesCollectionAdd
{
    public object Run() => new Basket { "x" };
}

public class UsesDeconstruct
{
    public int Run()
    {
        var (first, second) = Factory.MakePair();
        return first + second;
    }
}

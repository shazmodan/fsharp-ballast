using Fixture.Domain;
using Fixture.Ports;

namespace Fixture.App;

public class Service(IStore store)
{
    public void Run() => store.Save(Pricing.Price(new Order(1)).ToString());
}

public static class Main
{
    public static void Run(INotifier notifier, Service service)
    {
        service.Run();
        notifier.Notify("done");
    }
}

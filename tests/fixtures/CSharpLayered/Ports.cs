namespace Fixture.Ports;

public interface IStore
{
    void Save(string value);
}

public interface INotifier
{
    void Notify(string message);
}

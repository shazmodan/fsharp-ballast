namespace Fixture.Domain;

public record Order(int Id);

public static class Pricing
{
    public static int Price(Order order) => order.Id * 2;
}

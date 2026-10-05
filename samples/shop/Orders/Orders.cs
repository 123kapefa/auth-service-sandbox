using System.Collections.Concurrent;

namespace Orders;

/// <summary>
/// Права «Заказов» — одни и те же константы и в проверках (RequirePermissions), и в объявлении каталога
/// (AddAppCatalog в Program.cs): при старте сервис сам заводит их в сервисе входа, руками в админке ничего не нужно.
/// Роль = набор прав: customer — свои заказы, manager — все заказы и статусы.
/// </summary>
internal static class OrderPermissions
{
    public const string VIEW = "orders.view";
    public const string CREATE = "orders.create";
    public const string MANAGE = "orders.manage";
}

internal sealed class Order
{
    public required int Number { get; init; }

    public required Guid CustomerId { get; init; }

    public string? CustomerPhone { get; init; }

    public required string Item { get; init; }

    public string Status { get; set; } = "принят";

    /// <summary>Что с уведомлением: «SMS на +7…», «письмо на …» или почему не отправлено.</summary>
    public string? Notification { get; set; }

    public DateTimeOffset CreatedAt { get; } = DateTimeOffset.UtcNow;
}

/// <summary>Заказы — в памяти: мини-сервис для проверки встраивания, база ему не нужна.</summary>
internal sealed class OrderStore
{
    private readonly ConcurrentDictionary<int, Order> _orders = new();
    private int _last;

    public Order Add(Guid customerId, string? phone, string item)
    {
        Order order = new() { Number = Interlocked.Increment(ref _last), CustomerId = customerId, CustomerPhone = phone, Item = item };
        _orders[order.Number] = order;
        return order;
    }

    public Order? Find(int number) => _orders.GetValueOrDefault(number);

    public IEnumerable<Order> Of(Guid customerId) => All().Where(order => order.CustomerId == customerId);

    public IEnumerable<Order> All() => _orders.Values.OrderByDescending(order => order.Number);
}

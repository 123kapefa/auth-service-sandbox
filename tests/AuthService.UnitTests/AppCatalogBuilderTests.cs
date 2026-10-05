using AuthService.Client.AppCatalog;
using AuthService.Contracts.Services;
using Xunit;

namespace AuthService.UnitTests;

// Каталог приложения в коде: ошибки — исключением при старте, а не 403 в работе.
public sealed class AppCatalogBuilderTests
{
    [Fact]
    public void Build_PermissionsAndRoles()
    {
        DeclareCatalogRequest catalog = new AppCatalogBuilder()
            .Permission("orders.view", "Свои заказы")
            .Permission("orders.manage", "Все заказы")
            .Role("manager", "Сотрудник", "orders.view", "orders.manage", "orders.view")
            .Build();

        Assert.Equal(["orders.view", "orders.manage"], catalog.Permissions!.Select(permission => permission.Name!));
        Assert.Equal(["orders.view", "orders.manage"], catalog.Roles!.Single().Permissions!);
    }

    [Theory]
    [InlineData("orders")]
    [InlineData("Orders.view")]
    [InlineData("orders view")]
    public void Permission_BadName_Throws(string name) =>
        Assert.Throws<ArgumentException>(() => new AppCatalogBuilder().Permission(name, "x"));

    [Fact]
    public void Permission_Twice_Throws() =>
        Assert.Throws<ArgumentException>(() => new AppCatalogBuilder().Permission("orders.view", "a").Permission("orders.view", "b"));

    [Fact]
    public void Role_UndeclaredPermission_Throws()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(() =>
            new AppCatalogBuilder().Permission("orders.view", "a").Role("customer", "Покупатель", "orders.view", "orders.create"));

        Assert.Contains("orders.create", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Customer")]
    [InlineData("c")]
    [InlineData("orders.view")]
    public void Role_BadName_Throws(string name) =>
        Assert.Throws<ArgumentException>(() => new AppCatalogBuilder().Role(name, "x"));
}

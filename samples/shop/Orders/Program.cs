using AuthService.Client.AppCatalog;
using Orders;
using PlatformAuth;
using PlatformAuth.Redis;
using PlatformAuth.ServiceTokens;

// «Заказы» магазина. Покупатель входит через сервис входа проекта (страница — wwwroot, модуль входа сервиса входа,
// клиент orders-web) и приходит с токеном приложения «Заказы» (aud=orders): в нём его роли и права в «Заказах». О
// заказе сообщают «Уведомления» — туда «Заказы» ходят сами, со своим ключом доступа (service-orders) и правом
// «Уведомлений» notifications.send, которое админ выдал этому ключу.
WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Токены пользователей: подпись, издатель, адресат (секция Authentication) + права из токена.
builder.Services.AddPlatformJwtAuthentication(builder.Configuration);
builder.Services.AddPlatformAuthorization();
builder.Services.AddPlatformPermissions();
// Мгновенный отзыв (блокировка, «выйти везде», смена ролей) — отметки сервиса входа в Redis.
builder.Services.AddPlatformTokenRevocation(builder.Configuration);

// Ключ «Заказов» (секция ServiceToken) — токен в каждый запрос к «Уведомлениям».
builder.Services.AddPlatformServiceToken(builder.Configuration);
builder.Services.AddHttpClient<NotificationsClient>(client =>
        client.BaseAddress = new Uri(builder.Configuration["Notifications:BaseUrl"]
                                     ?? throw new InvalidOperationException("Не задан адрес «Уведомлений» Notifications:BaseUrl")))
    .AddServiceToken();
builder.Services.AddSingleton<OrderStore>();

// Права и роли «Заказов» — из кода, а не руками в админке: при старте сервис отправляет их в сервис входа своим
// ключом (тому нужно право «Объявлять права и роли своего приложения»). Права заводятся и не удаляются в админке,
// пока их проверяет код; роли заводятся один раз — дальше их составом управляет админ. Подробно — AppCatalogBuilder.
builder.Services.AddAppCatalog(builder.Configuration, catalog => catalog
    .Permission(OrderPermissions.VIEW, "Свои заказы")
    .Permission(OrderPermissions.CREATE, "Оформить заказ")
    .Permission(OrderPermissions.MANAGE, "Все заказы и смена статуса")
    .Role("customer", "Покупатель", OrderPermissions.VIEW, OrderPermissions.CREATE)
    .Role("manager", "Сотрудник магазина", OrderPermissions.VIEW, OrderPermissions.MANAGE));

WebApplication app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health/live", () => Results.Ok());

// Странице — где вход: realm проекта, клиент входа «Заказов» и сервис входа (модуль входа, личный кабинет).
app.MapGet("/config", (IConfiguration configuration) => new
{
    authority = configuration["Authentication:Authority"],
    clientId = configuration["Shop:LoginClient"] ?? "orders-web",
    app = configuration["Authentication:Audience"],
    project = configuration["Shop:Name"] ?? "Магазин",
    authService = configuration["Shop:AuthServiceUrl"]?.TrimEnd('/'),
});

// Кто вошёл: из токена, без похода в сервис входа.
app.MapGet("/api/me", (CurrentUser user) => new
{
    id = user.Id,
    phone = user.Phone,
    mode = user.ActiveMode,
    roles = user.Roles,
    permissions = user.Permissions.Order(StringComparer.Ordinal),
}).RequireUser();

app.MapGet("/api/orders", (CurrentUser user, OrderStore orders) => orders.Of(user.Id!.Value))
    .RequirePermissions(OrderPermissions.VIEW);

app.MapPost("/api/orders", async (NewOrder request, CurrentUser user, OrderStore orders, NotificationsClient notifications, CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Item))
    {
        return Results.BadRequest(new { error = "Что заказать — item" });
    }

    Order order = orders.Add(user.Id!.Value, user.Phone, request.Item.Trim());
    // Заказ принят в любом случае; не дошло уведомление — видно в заказе.
    order.Notification = await notifications.SendAsync(order.CustomerId, $"Заказ №{order.Number} принят: {order.Item}", cancellationToken);
    return Results.Created($"/api/orders/{order.Number}", order);
}).RequirePermissions(OrderPermissions.CREATE);

// Сотрудник магазина (режим manager в «Заказах»): все заказы и смена статуса.
app.MapGet("/api/orders/all", (OrderStore orders) => orders.All())
    .RequirePermissions(OrderPermissions.MANAGE);

app.MapPost("/api/orders/{number:int}/status", async (int number, NewStatus request, OrderStore orders, NotificationsClient notifications, CancellationToken cancellationToken) =>
{
    if (orders.Find(number) is not { } order)
    {
        return Results.NotFound();
    }

    if (string.IsNullOrWhiteSpace(request.Status))
    {
        return Results.BadRequest(new { error = "Новый статус — status" });
    }

    order.Status = request.Status.Trim();
    order.Notification = await notifications.SendAsync(order.CustomerId, $"Заказ №{order.Number} ({order.Item}): {order.Status}", cancellationToken);
    return Results.Ok(order);
}).RequirePermissions(OrderPermissions.MANAGE);

app.Run();

internal sealed record NewOrder(string? Item);

internal sealed record NewStatus(string? Status);

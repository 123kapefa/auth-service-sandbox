using AuthService.Client;
using Notifications;
using PlatformAuth;
using PlatformAuth.Redis;

// «Уведомления» магазина: внутренний сервис, людям сюда хода нет. Присылать уведомления может только сервис проекта
// по своему ключу и только из списка Notifications:AllowedServices («Заказы»). Кому и куда писать — узнаёт у сервиса
// входа своим ключом (право service.users.read): телефон и почта получателя.
WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddPlatformJwtAuthentication(builder.Configuration);
builder.Services.AddPlatformAuthorization();
builder.Services.AddPlatformPermissions();
// Отозванный ключ перестаёт действовать сразу: сервис входа гасит его токены отметкой в Redis.
builder.Services.AddPlatformTokenRevocation(builder.Configuration);
builder.Services.AddAuthServiceClient(builder.Configuration);

builder.Services.Configure<NotificationOptions>(builder.Configuration.GetSection(NotificationOptions.SECTION_NAME));
builder.Services.AddSingleton<NotificationLog>();
builder.Services.AddSingleton<Delivery>();

WebApplication app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health/live", () => Results.Ok());

// Свой список сервисов: право «присылать уведомления» в ключе не выдать — права ключа есть только в сервисе входа.
RouteGroupBuilder api = app.MapGroup("/api/notifications")
    .RequireService()
    .RequireAuthorization(policy => policy.RequireClaim(
        AuthClaims.CLIENT_ID,
        app.Configuration.GetSection("Notifications:AllowedServices").Get<string[]>() ?? []));

api.MapPost(string.Empty, async (NewNotification request, CurrentUser caller, IAuthServiceClient authService, Delivery delivery,
    NotificationLog log, CancellationToken cancellationToken) =>
{
    if (request.UserId == Guid.Empty || string.IsNullOrWhiteSpace(request.Text))
    {
        return Results.BadRequest(new { error = "Кому (userId) и что (text)" });
    }

    var user = await authService.GetUserAsync(request.UserId, cancellationToken);
    if (user.IsFailure)
    {
        // Пользователя нет, ключ «Уведомлений» не принят или сервис входа недоступен — отправлять некуда.
        return Results.Problem(user.Error.Messages[0].Message, statusCode: StatusCodes.Status502BadGateway);
    }

    if (!user.Value.Enabled)
    {
        return Results.UnprocessableEntity(new { error = "Аккаунт заблокирован — уведомления не отправляются" });
    }

    Notification sent = await delivery.SendAsync(user.Value, request.Text.Trim(), caller.ServiceClientId!, cancellationToken);
    log.Add(sent);
    return Results.Ok(sent);
});

// Журнал: что и кому отправлено (для проверки и отладки).
api.MapGet(string.Empty, (Guid? userId, NotificationLog log) => log.Of(userId));

app.Run();

internal sealed record NewNotification(Guid UserId, string? Text);

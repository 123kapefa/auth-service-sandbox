using AuthService.Client;
using AuthService.Client.AppCatalog;
using Notifications;
using PlatformAuth;
using PlatformAuth.Redis;

// «Уведомления» магазина: внутренний сервис, людям сюда хода нет. Присылать уведомления может только сервис проекта
// по своему ключу, если админ выдал его ключу право «Уведомлений» notifications.send («Сервисы и ключи» → «Права»).
// Токен такого ключа адресован и «Уведомлениям» (aud=notifications), права в нём — по приложениям: здесь видны только
// права в «Уведомлениях». Кому и куда писать — узнаёт у сервиса входа своим ключом (service.users.read).
WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddPlatformJwtAuthentication(builder.Configuration);
builder.Services.AddPlatformAuthorization();
builder.Services.AddPlatformPermissions();
// Отозванный ключ перестаёт действовать сразу: сервис входа гасит его токены отметкой в Redis.
builder.Services.AddPlatformTokenRevocation(builder.Configuration);
builder.Services.AddAuthServiceClient(builder.Configuration);
// Свои права — из кода: их выдают ключам других сервисов. Ролей для людей у «Уведомлений» нет.
builder.Services.AddAppCatalog(builder.Configuration, catalog => catalog
    .Permission(NotificationPermissions.SEND, "Присылать уведомления")
    .Permission(NotificationPermissions.READ, "Читать журнал уведомлений"));

builder.Services.Configure<NotificationOptions>(builder.Configuration.GetSection(NotificationOptions.SECTION_NAME));
builder.Services.AddSingleton<NotificationLog>();
builder.Services.AddSingleton<Delivery>();

WebApplication app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health/live", () => Results.Ok());

app.MapPost("/api/notifications", async (NewNotification request, CurrentUser caller, IAuthServiceClient authService, Delivery delivery,
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
}).RequireService(NotificationPermissions.SEND);

// Журнал: что и кому отправлено (для проверки и отладки).
app.MapGet("/api/notifications", (Guid? userId, NotificationLog log) => log.Of(userId))
    .RequireService(NotificationPermissions.READ);

app.Run();

internal sealed record NewNotification(Guid UserId, string? Text);

internal static class NotificationPermissions
{
    public const string SEND = "notifications.send";
    public const string READ = "notifications.read";
}

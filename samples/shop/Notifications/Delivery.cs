using System.Collections.Concurrent;
using System.Net.Mail;
using AuthService.Contracts.Services;
using Microsoft.Extensions.Options;

namespace Notifications;

internal sealed class NotificationOptions
{
    public const string SECTION_NAME = "Notifications";

    public string SmtpHost { get; init; } = "localhost";

    public int SmtpPort { get; init; } = 1025;

    public string From { get; init; } = "no-reply@shop.test";
}

/// <param name="Delivery">Куда ушло, словами: «письмо на …» или «SMS на +7…».</param>
/// <param name="Sender">client_id сервиса, приславшего уведомление.</param>
internal sealed record Notification(Guid Id, Guid UserId, string Text, string Delivery, string Sender, DateTimeOffset SentAt);

/// <summary>
/// Доставка: есть почта — письмо (в песочнице — Mailpit), нет — «SMS» строкой в лог, как SMS-коды Keycloak на стенде.
/// </summary>
internal sealed partial class Delivery(IOptions<NotificationOptions> options, ILogger<Delivery> logger)
{
    public async Task<Notification> SendAsync(ServiceUserResponse user, string text, string sender, CancellationToken cancellationToken)
    {
        string delivery;
        if (!string.IsNullOrWhiteSpace(user.Email))
        {
            NotificationOptions settings = options.Value;
            using SmtpClient smtp = new(settings.SmtpHost, settings.SmtpPort);
            using MailMessage message = new(settings.From, user.Email, text, text);
            await smtp.SendMailAsync(message, cancellationToken);
            delivery = $"письмо на {user.Email}";
        }
        else
        {
            LogSms(logger, user.Phone ?? user.Username, text);
            delivery = $"SMS на {user.Phone ?? user.Username}";
        }

        return new Notification(Guid.NewGuid(), user.Id, text, delivery, sender, DateTimeOffset.UtcNow);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "[DEV SMS] {Phone}: {Text}")]
    private static partial void LogSms(ILogger logger, string phone, string text);
}

/// <summary>Отправленные уведомления — в памяти.</summary>
internal sealed class NotificationLog
{
    private readonly ConcurrentQueue<Notification> _sent = new();

    public void Add(Notification notification) => _sent.Enqueue(notification);

    public IEnumerable<Notification> Of(Guid? userId) =>
        _sent.Where(notification => userId is null || notification.UserId == userId).Reverse();
}

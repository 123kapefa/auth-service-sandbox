using System.Net;
using System.Net.Mail;
using AuthService.Core.Abstractions;
using AuthService.Infrastructure.Postgres.Options;
using Microsoft.Extensions.Options;

namespace AuthService.Infrastructure.Postgres.Email;

public sealed class SmtpEmailSender(IOptions<SmtpOptions> options) : IEmailSender
{
    public async Task SendAsync(string recipient, string subject, string htmlBody, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var settings = options.Value;
        using var client = new SmtpClient(settings.Host, settings.Port)
        {
            EnableSsl = settings.EnableSsl,
            Credentials = string.IsNullOrWhiteSpace(settings.UserName)
                ? CredentialCache.DefaultNetworkCredentials
                : new NetworkCredential(settings.UserName, settings.Password)
        };
        using var message = new MailMessage
        {
            From = new MailAddress(settings.FromAddress, settings.FromName),
            Subject = subject,
            Body = htmlBody,
            IsBodyHtml = true
        };
        message.To.Add(recipient);
        await client.SendMailAsync(message, cancellationToken);
    }
}

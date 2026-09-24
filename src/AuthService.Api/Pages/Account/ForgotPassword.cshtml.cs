using AuthService.Core.Abstractions;
using AuthService.Domain.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AuthService.Api.Pages;

public sealed class ForgotPasswordModel(
    UserManager<Account> userManager,
    IEmailSender emailSender,
    ISecurityAuditWriter auditWriter) : PageModel
{
    [BindProperty]
    public string? Email { get; set; }

    public string? Message { get; private set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var account = string.IsNullOrWhiteSpace(Email) ? null : await userManager.FindByEmailAsync(Email);
        if (account is not null && account.EmailConfirmed)
        {
            var code = await userManager.GeneratePasswordResetTokenAsync(account);
            var link = Url.Page("/Account/ResetPassword", null, new { userId = account.Id, code }, Request.Scheme)!;
            await emailSender.SendAsync(account.Email!, "Сброс пароля", $"<p>Сбросьте пароль: <a href=\"{link}\">сбросить</a>.</p>", cancellationToken);
            await auditWriter.WriteAsync("account.password_reset_requested", account.Id, ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken: cancellationToken);
        }

        Message = "Если учётная запись существует, письмо уже отправлено.";
        return Page();
    }
}

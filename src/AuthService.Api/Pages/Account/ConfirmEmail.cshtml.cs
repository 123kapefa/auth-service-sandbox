using AuthService.Core.Abstractions;
using AuthService.Domain.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AuthService.Api.Pages;

public sealed class ConfirmEmailModel(UserManager<Account> userManager, ISecurityAuditWriter auditWriter) : PageModel
{
    public bool Succeeded { get; private set; }
    public string Message { get; private set; } = "Некорректная ссылка подтверждения.";

    public async Task OnGetAsync(Guid? userId, string? code, CancellationToken cancellationToken)
    {
        if (userId is null || string.IsNullOrWhiteSpace(code))
        {
            return;
        }

        var account = await userManager.FindByIdAsync(userId.Value.ToString());
        if (account is null)
        {
            return;
        }

        var result = await userManager.ConfirmEmailAsync(account, code);
        Succeeded = result.Succeeded;
        Message = result.Succeeded ? "Email подтверждён. Теперь можно войти." : "Ссылка подтверждения недействительна или истекла.";
        if (result.Succeeded)
        {
            await auditWriter.WriteAsync("account.email_confirmed", account.Id, ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken: cancellationToken);
        }
    }
}

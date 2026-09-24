using AuthService.Core.Abstractions;
using AuthService.Domain.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OpenIddict.Abstractions;

namespace AuthService.Api.Pages;

[Authorize]
public sealed class SecurityModel(
    UserManager<Account> userManager,
    IOpenIddictTokenManager tokenManager,
    ISecurityAuditWriter auditWriter) : PageModel
{
    public string? Email { get; private set; }
    public bool MfaEnabled { get; private set; }
    public string? Message { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var account = await userManager.GetUserAsync(User);
        if (account is null)
        {
            return Challenge();
        }

        Email = account.Email;
        MfaEnabled = await userManager.GetTwoFactorEnabledAsync(account);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var account = await userManager.GetUserAsync(User);
        if (account is null)
        {
            return Challenge();
        }

        await tokenManager.RevokeBySubjectAsync(account.Id.ToString(), cancellationToken);
        await userManager.UpdateSecurityStampAsync(account);
        await auditWriter.WriteAsync("account.sessions_revoked", account.Id, ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken: cancellationToken);
        Email = account.Email;
        MfaEnabled = await userManager.GetTwoFactorEnabledAsync(account);
        Message = "Все refresh-сессии отозваны.";
        return Page();
    }
}

using AuthService.Core.Abstractions;
using AuthService.Domain.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AuthService.Api.Pages;

public sealed class MfaVerifyModel(SignInManager<Account> signInManager, ISecurityAuditWriter auditWriter) : PageModel
{
    [BindProperty]
    public string? Code { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public string? Error { get; private set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var result = await signInManager.TwoFactorAuthenticatorSignInAsync(
            Code?.Replace(" ", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal) ?? string.Empty,
            false,
            false);
        if (!result.Succeeded)
        {
            Error = result.IsLockedOut ? "Учётная запись временно заблокирована." : "Код неверный.";
            return Page();
        }

        var account = await signInManager.GetTwoFactorAuthenticationUserAsync();
        if (account is not null)
        {
            await auditWriter.WriteAsync("account.mfa_verified", account.Id, ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken: cancellationToken);
        }

        return LocalRedirect(!string.IsNullOrWhiteSpace(ReturnUrl) && Url.IsLocalUrl(ReturnUrl) ? ReturnUrl : "/Account/Security");
    }
}

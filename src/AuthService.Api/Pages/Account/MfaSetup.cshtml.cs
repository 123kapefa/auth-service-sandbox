using AuthService.Core.Abstractions;
using AuthService.Domain.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AuthService.Api.Pages;

[Authorize]
public sealed class MfaSetupModel(UserManager<Account> userManager, ISecurityAuditWriter auditWriter) : PageModel
{
    [BindProperty]
    public string? Code { get; set; }

    public string? SharedKey { get; private set; }
    public bool Succeeded { get; private set; }
    public string? Message { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var account = await userManager.GetUserAsync(User);
        if (account is null)
        {
            return Challenge();
        }

        SharedKey = await EnsureAuthenticatorKeyAsync(account);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var account = await userManager.GetUserAsync(User);
        if (account is null)
        {
            return Challenge();
        }

        SharedKey = await EnsureAuthenticatorKeyAsync(account);
        var valid = !string.IsNullOrWhiteSpace(Code) && await userManager.VerifyTwoFactorTokenAsync(
            account,
            userManager.Options.Tokens.AuthenticatorTokenProvider,
            Code.Replace(" ", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal));
        if (!valid)
        {
            Message = "Код неверный.";
            return Page();
        }

        await userManager.SetTwoFactorEnabledAsync(account, true);
        var recoveryCodes = await userManager.GenerateNewTwoFactorRecoveryCodesAsync(account, 10);
        Succeeded = true;
        Message = $"MFA включена. Сохраните recovery codes: {string.Join(" ", recoveryCodes ?? [])}";
        await auditWriter.WriteAsync("account.mfa_enabled", account.Id, ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken: cancellationToken);
        return Page();
    }

    private async Task<string> EnsureAuthenticatorKeyAsync(Account account)
    {
        var key = await userManager.GetAuthenticatorKeyAsync(account);
        if (!string.IsNullOrWhiteSpace(key))
        {
            return key;
        }

        await userManager.ResetAuthenticatorKeyAsync(account);
        return (await userManager.GetAuthenticatorKeyAsync(account))!;
    }
}

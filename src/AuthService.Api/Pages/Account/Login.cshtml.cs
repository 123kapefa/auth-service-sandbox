using AuthService.Core.Abstractions;
using AuthService.Domain.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AuthService.Api.Pages;

public sealed class LoginModel(
    UserManager<Account> userManager,
    SignInManager<Account> signInManager,
    ISecurityAuditWriter auditWriter) : PageModel
{
    [BindProperty]
    public LoginInput Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public string? Error { get; private set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var account = string.IsNullOrWhiteSpace(Input.Email) ? null : await userManager.FindByEmailAsync(Input.Email);
        if (account is null)
        {
            await auditWriter.WriteAsync("account.login_failed", ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken: cancellationToken);
            Error = "Неверные учётные данные.";
            return Page();
        }

        var result = await signInManager.PasswordSignInAsync(account.UserName!, Input.Password ?? string.Empty, false, lockoutOnFailure: true);
        if (result.RequiresTwoFactor)
        {
            return RedirectToPage("MfaVerify", new { ReturnUrl });
        }

        if (!result.Succeeded)
        {
            await auditWriter.WriteAsync(result.IsLockedOut ? "account.locked_out" : "account.login_failed", account.Id, ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken: cancellationToken);
            Error = result.IsLockedOut
                ? "Учётная запись временно заблокирована."
                : result.IsNotAllowed
                    ? "Подтвердите email, чтобы войти."
                    : "Неверные учётные данные.";
            return Page();
        }

        await auditWriter.WriteAsync("account.login_succeeded", account.Id, ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken: cancellationToken);
        return LocalRedirect(IsSafeReturnUrl(ReturnUrl) ? ReturnUrl! : "/Account/Security");
    }

    private bool IsSafeReturnUrl(string? returnUrl) => !string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl);

    public sealed class LoginInput
    {
        public string? Email { get; init; }
        public string? Password { get; init; }
    }
}

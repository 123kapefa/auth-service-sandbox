using AuthService.Core.Abstractions;
using AuthService.Domain.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AuthService.Api.Pages;

public sealed class ResetPasswordModel(UserManager<Account> userManager, ISecurityAuditWriter auditWriter) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public Guid? UserId { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Code { get; set; }

    [BindProperty]
    public string? Password { get; set; }

    public bool Succeeded { get; private set; }
    public string? Message { get; private set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (UserId is null || string.IsNullOrWhiteSpace(Code) || string.IsNullOrWhiteSpace(Password))
        {
            Message = "Некорректный запрос.";
            return Page();
        }

        var account = await userManager.FindByIdAsync(UserId.Value.ToString());
        if (account is null)
        {
            Message = "Некорректный запрос.";
            return Page();
        }

        var result = await userManager.ResetPasswordAsync(account, Code, Password);
        Succeeded = result.Succeeded;
        Message = result.Succeeded ? "Пароль изменён. Все refresh-сессии отозваны." : "Не удалось изменить пароль.";
        if (result.Succeeded)
        {
            await userManager.UpdateSecurityStampAsync(account);
            await auditWriter.WriteAsync("account.password_reset", account.Id, ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken: cancellationToken);
        }

        return Page();
    }
}

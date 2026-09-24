using AuthService.Core.Abstractions;
using AuthService.Domain.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AuthService.Api.Pages;

public sealed class LogoutModel(UserManager<Account> userManager, ISecurityAuditWriter auditWriter) : PageModel
{
    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var account = await userManager.GetUserAsync(User);
        if (account is not null)
        {
            await auditWriter.WriteAsync("account.logout", account.Id, ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken: cancellationToken);
        }

        await HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
        return RedirectToPage("Login");
    }
}

using AuthService.Core.Abstractions;
using AuthService.Core.Features.Accounts.Register;
using AuthService.Domain.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AuthService.Api.Pages;

public sealed class RegisterModel(
    RegisterAccountHandler handler,
    UserManager<Account> userManager,
    IEmailSender emailSender) : PageModel
{
    [BindProperty]
    public RegisterInput Input { get; set; } = new();

    public List<string> Errors { get; } = [];

    public string? Message { get; private set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var command = new RegisterAccountCommand(Input.Email ?? string.Empty, Input.UserName ?? string.Empty, Input.DisplayName ?? string.Empty, Input.Password ?? string.Empty);
        var result = await handler.HandleAsync(command, HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken);
        if (!result.Succeeded)
        {
            if (result.Errors.All(error => error.StartsWith("identity.", StringComparison.Ordinal) || error.Contains("Duplicate", StringComparison.Ordinal)))
            {
                Message = "Если адрес доступен, письмо с подтверждением уже отправлено.";
                return Page();
            }

            Errors.AddRange(result.Errors);
            return Page();
        }

        var account = result.Account!;
        var token = await userManager.GenerateEmailConfirmationTokenAsync(account);
        var link = Url.Page("/Account/ConfirmEmail", null, new { userId = account.Id, code = token }, Request.Scheme)!;
        await emailSender.SendAsync(account.Email!, "Подтвердите email", $"<p>Подтвердите адрес: <a href=\"{link}\">подтвердить</a>.</p>", cancellationToken);
        Message = "Проверьте почту и подтвердите адрес, чтобы войти.";
        return Page();
    }

    public sealed class RegisterInput
    {
        public string? Email { get; init; }
        public string? UserName { get; init; }
        public string? DisplayName { get; init; }
        public string? Password { get; init; }
    }
}

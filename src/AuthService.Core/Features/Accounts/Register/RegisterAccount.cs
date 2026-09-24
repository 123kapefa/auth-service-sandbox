using AuthService.Core.Abstractions;
using AuthService.Domain.Identity;
using Microsoft.AspNetCore.Identity;

namespace AuthService.Core.Features.Accounts.Register;

public sealed record RegisterAccountCommand(string Email, string UserName, string DisplayName, string Password);

public sealed record RegisterAccountResult(bool Succeeded, IReadOnlyCollection<string> Errors, Account? Account);

public sealed class RegisterAccountHandler(
    UserManager<Account> userManager,
    ISecurityAuditWriter auditWriter)
{
    public async Task<RegisterAccountResult> HandleAsync(
        RegisterAccountCommand command,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        var errors = Validate(command);
        if (errors.Count > 0)
        {
            return new RegisterAccountResult(false, errors, null);
        }

        var account = new Account(command.Email, command.UserName, command.DisplayName);
        var result = await userManager.CreateAsync(account, command.Password);
        if (!result.Succeeded)
        {
            return new RegisterAccountResult(false, result.Errors.Select(error => error.Code).ToArray(), null);
        }

        await auditWriter.WriteAsync("account.registered", account.Id, ipAddress: ipAddress, cancellationToken: cancellationToken);
        return new RegisterAccountResult(true, [], account);
    }

    private static List<string> Validate(RegisterAccountCommand command)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(command.Email) || command.Email.Length > 254 || !command.Email.Contains('@'))
        {
            errors.Add("email.invalid");
        }

        if (string.IsNullOrWhiteSpace(command.UserName) || command.UserName.Length > 30)
        {
            errors.Add("username.invalid");
        }

        if (string.IsNullOrWhiteSpace(command.DisplayName) || command.DisplayName.Length > 80)
        {
            errors.Add("display_name.invalid");
        }

        if (string.IsNullOrEmpty(command.Password) || command.Password.Length < 12)
        {
            errors.Add("password.invalid");
        }

        return errors;
    }
}

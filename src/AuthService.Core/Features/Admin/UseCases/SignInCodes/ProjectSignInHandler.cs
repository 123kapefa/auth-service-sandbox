using System.Globalization;
using AuthService.Contracts.Admin;
using AuthService.Core.Abstractions;
using AuthService.Domain.AuditLog;
using CSharpFunctionalExtensions;
using FluentValidation;
using FluentValidation.Results;
using SharedKernel;
using Validation;

namespace AuthService.Core.Features.Admin.UseCases.SignInCodes;

/// <summary>
/// Способы входа проекта: что вообще доступно людям, 2FA, переходный период. Хранятся в Keycloak (realm), меняются
/// через плагин platform-sign-in; плагин применяет их при каждом входе. Изменение — в журнал (что поменялось).
/// </summary>
public sealed class ProjectSignInHandler(
    IProjectSignIn signIn,
    IValidator<SetSignInMethodsRequest> validator,
    AdminAudit audit)
{
    /// <summary>Имя «объекта» в журнале: раздел админки.</summary>
    public const string AUDIT_NAME = "Способы входа";

    public async Task<Result<SignInMethodsResponse, Error>> GetAsync(CancellationToken cancellationToken)
    {
        Result<ProjectSignInSettings, Error> current = await signIn.GetAsync(cancellationToken);
        return current.IsSuccess ? ToResponse(current.Value) : current.Error;
    }

    public async Task<Result<SignInMethodsResponse, Error>> SetAsync(SetSignInMethodsRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidationResult validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return validation.ToError();
        }

        Result<ProjectSignInSettings, Error> current = await signIn.GetAsync(cancellationToken);
        if (current.IsFailure)
        {
            return current.Error;
        }

        var wanted = new ProjectSignInSettings(
            ProjectSignInRules.Methods.ToDictionary(
                method => method,
                method => ToMethod(request.Methods![method]!),
                StringComparer.Ordinal),
            string.Equals(request.TwoFactor, ProjectSignInRules.TWO_FACTOR_REQUIRED, StringComparison.Ordinal));
        Dictionary<string, object?> changes = Changes(current.Value, wanted);
        if (changes.Count == 0)
        {
            return ToResponse(current.Value);
        }

        Result<ProjectSignInSettings, Error> saved = await signIn.SetAsync(wanted, cancellationToken);
        if (saved.IsFailure)
        {
            return saved.Error;
        }

        await audit.RecordAsync(
            AuditActions.PROJECT_SIGN_IN_CHANGED,
            AuditActions.PROJECT_OBJECT,
            Guid.Empty,
            AUDIT_NAME,
            changes,
            CancellationToken.None);
        return ToResponse(saved.Value);
    }

    public async Task<Result<SignInMethodUsersResponse, Error>> AffectedAsync(string method, CancellationToken cancellationToken)
    {
        if (!ProjectSignInRules.Methods.Contains(method, StringComparer.Ordinal))
        {
            return ProjectSignInErrors.MethodNotFound(method);
        }

        Result<SignInMethodUsers, Error> users = await signIn.GetAffectedAsync(method, cancellationToken);
        return users.IsSuccess ? new SignInMethodUsersResponse(users.Value.Method, users.Value.Users, users.Value.Only) : users.Error;
    }

    private static ProjectSignInMethod ToMethod(SetSignInMethodState state) =>
        string.Equals(state.Status, ProjectSignInRules.TRANSITION, StringComparison.Ordinal)
            ? new ProjectSignInMethod(ProjectSignInRules.TRANSITION, DateOnly.ParseExact(state.Until!, "yyyy-MM-dd", CultureInfo.InvariantCulture))
            : new ProjectSignInMethod(state.Status!);

    // Только изменённые способы: { old, new } — «on», «off», «transition до 2026-11-05».
    private static Dictionary<string, object?> Changes(ProjectSignInSettings old, ProjectSignInSettings wanted)
    {
        var changes = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (string method in ProjectSignInRules.Methods)
        {
            string before = Describe(old.Methods.GetValueOrDefault(method));
            string after = Describe(wanted.Methods[method]);
            if (!string.Equals(before, after, StringComparison.Ordinal))
            {
                changes[method] = new { old = before, @new = after };
            }
        }

        if (old.TwoFactorRequired != wanted.TwoFactorRequired)
        {
            changes["twoFactor"] = new { old = TwoFactor(old.TwoFactorRequired), @new = TwoFactor(wanted.TwoFactorRequired) };
        }

        return changes;
    }

    private static string Describe(ProjectSignInMethod? method) =>
        method is null ? ProjectSignInRules.ON
        : string.Equals(method.Status, ProjectSignInRules.TRANSITION, StringComparison.Ordinal)
            ? $"{ProjectSignInRules.TRANSITION} до {method.Until?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}"
            : method.Status;

    private static string TwoFactor(bool required) =>
        required ? ProjectSignInRules.TWO_FACTOR_REQUIRED : ProjectSignInRules.TWO_FACTOR_OPTIONAL;

    private static SignInMethodsResponse ToResponse(ProjectSignInSettings settings) =>
        new(
            settings.Methods.ToDictionary(
                pair => pair.Key,
                pair => new SignInMethodState(
                    pair.Value.Status,
                    pair.Value.Until?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    pair.Value.Since,
                    pair.Value.Configured),
                StringComparer.Ordinal),
            TwoFactor(settings.TwoFactorRequired));
}

using System.Globalization;
using AuthService.Contracts.Admin;
using AuthService.Core.Abstractions;
using AuthService.Domain.AuditLog;
using CSharpFunctionalExtensions;
using FluentValidation;
using FluentValidation.Results;
using SharedKernel;
using Validation;

namespace AuthService.Core.Features.Admin.UseCases.BrandingSettings;

/// <summary>
/// Оформление приложения поверх проекта: своё название, логотип, цвет (null — как у проекта; тема — всегда проекта).
/// Хранится у клиента приложения в Keycloak, показывается на страницах входа в приложение, в его письмах и в кабинете,
/// открытом из него. Приложение — клиент с ролями (AppCatalog). Изменение — в журнал проекта и приложения.
/// </summary>
public sealed class AppBrandingHandler(
    IProjectBranding branding,
    AppCatalog catalog,
    IValidator<SetAppBrandingRequest> validator,
    AdminAudit audit)
{
    public async Task<Result<AppBrandingResponse, Error>> GetAsync(string clientId, CancellationToken cancellationToken)
    {
        Result<AppBranding, Error> own = await LoadAsync(clientId, cancellationToken);
        if (own.IsFailure)
        {
            return own.Error;
        }

        Result<ProjectBranding, Error> project = await branding.GetAsync(cancellationToken);
        return project.IsSuccess ? ToResponse(clientId, own.Value, project.Value) : project.Error;
    }

    public async Task<Result<AppBrandingResponse, Error>> SetAsync(string clientId, SetAppBrandingRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidationResult validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return validation.ToError();
        }

        Result<AppBranding, Error> current = await LoadAsync(clientId, cancellationToken);
        if (current.IsFailure)
        {
            return current.Error;
        }

        Result<ProjectBranding, Error> project = await branding.GetAsync(cancellationToken);
        if (project.IsFailure)
        {
            return project.Error;
        }

        var wanted = new AppBranding(request.Name?.Trim(), request.Logo, request.Color?.ToLower(CultureInfo.InvariantCulture));
        if (wanted == current.Value)
        {
            return ToResponse(clientId, wanted, project.Value);
        }

        UnitResult<Error> saved = await branding.SetAppAsync(clientId, wanted, cancellationToken);
        if (saved.IsFailure)
        {
            return saved.Error;
        }

        await audit.RecordAsync(
            AuditActions.APP_BRANDING_CHANGED,
            AuditActions.APP_OBJECT,
            Guid.Empty,
            clientId,
            Changes(current.Value, wanted),
            CancellationToken.None,
            clientId);
        return ToResponse(clientId, wanted, project.Value);
    }

    // Сначала — что это приложение (клиент с ролями), а не любой клиент realm (служебный, вход сервиса).
    private async Task<Result<AppBranding, Error>> LoadAsync(string clientId, CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<AppRole>, Error> app = await catalog.LoadAsync(clientId, cancellationToken);
        return app.IsFailure ? app.Error : await branding.GetAppAsync(clientId, cancellationToken);
    }

    // Только изменённые поля, { old, new }; null — «как у проекта»; логотип — только был ли и есть ли.
    private static Dictionary<string, object?> Changes(AppBranding old, AppBranding wanted)
    {
        var changes = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (!string.Equals(old.Name, wanted.Name, StringComparison.Ordinal))
        {
            changes["name"] = new { old = old.Name, @new = wanted.Name };
        }

        if (!string.Equals(old.Logo, wanted.Logo, StringComparison.Ordinal))
        {
            changes["logo"] = new { old = old.Logo is not null, @new = wanted.Logo is not null };
        }

        if (!string.Equals(old.Color, wanted.Color, StringComparison.Ordinal))
        {
            changes["color"] = new { old = old.Color, @new = wanted.Color };
        }

        return changes;
    }

    private static AppBrandingResponse ToResponse(string clientId, AppBranding own, ProjectBranding project) =>
        new(clientId, own.Name, own.Logo, own.Color, new BrandingResponse(project.Name, project.Logo, project.Color, project.Theme));
}

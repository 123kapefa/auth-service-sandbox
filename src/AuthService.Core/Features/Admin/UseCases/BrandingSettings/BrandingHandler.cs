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
/// Оформление проекта: название, логотип, акцент (для светлой и тёмной темы) и тема страниц входа. Хранится в Keycloak (realm), меняется через
/// плагин platform-branding; страницы входа подхватывают его при следующем открытии. Изменение — в журнал (без
/// самой картинки: только был ли логотип и есть ли теперь).
/// </summary>
public sealed class BrandingHandler(IProjectBranding branding, IValidator<SetBrandingRequest> validator, AdminAudit audit)
{
    public async Task<Result<BrandingResponse, Error>> GetAsync(CancellationToken cancellationToken)
    {
        Result<ProjectBranding, Error> current = await branding.GetAsync(cancellationToken);
        return current.IsSuccess ? ToResponse(current.Value) : current.Error;
    }

    public async Task<Result<BrandingResponse, Error>> SetAsync(SetBrandingRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidationResult validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return validation.ToError();
        }

        Result<ProjectBranding, Error> current = await branding.GetAsync(cancellationToken);
        if (current.IsFailure)
        {
            return current.Error;
        }

        var wanted = new ProjectBranding(
            request.Name!.Trim(),
            request.Logo,
            request.Color?.ToLower(CultureInfo.InvariantCulture),
            request.Theme!,
            request.ColorDark?.ToLower(CultureInfo.InvariantCulture));
        if (wanted == current.Value)
        {
            return ToResponse(wanted);
        }

        UnitResult<Error> saved = await branding.SetAsync(wanted, cancellationToken);
        if (saved.IsFailure)
        {
            return saved.Error;
        }

        await audit.RecordAsync(
            AuditActions.PROJECT_BRANDING_CHANGED,
            AuditActions.PROJECT_OBJECT,
            Guid.Empty,
            wanted.Name,
            Changes(current.Value, wanted),
            CancellationToken.None);
        return ToResponse(wanted);
    }

    // Только изменённые поля, в виде { old, new }, как в остальном журнале.
    private static Dictionary<string, object?> Changes(ProjectBranding old, ProjectBranding wanted)
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

        if (!string.Equals(old.ColorDark, wanted.ColorDark, StringComparison.Ordinal))
        {
            changes["colorDark"] = new { old = old.ColorDark, @new = wanted.ColorDark };
        }

        if (!string.Equals(old.Theme, wanted.Theme, StringComparison.Ordinal))
        {
            changes["theme"] = new { old = old.Theme, @new = wanted.Theme };
        }

        return changes;
    }

    private static BrandingResponse ToResponse(ProjectBranding branding) =>
        new(branding.Name, branding.Logo, branding.Color, branding.Theme, branding.ColorDark);
}

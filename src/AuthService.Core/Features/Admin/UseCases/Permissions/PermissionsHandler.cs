using AuthService.Contracts.Admin;
using AuthService.Core.Abstractions;
using AuthService.Domain.AuditLog;
using CSharpFunctionalExtensions;
using FluentValidation;
using FluentValidation.Results;
using SharedKernel;
using Validation;

namespace AuthService.Core.Features.Admin.UseCases.Permissions;

/// <summary>
/// Права приложения: системные (source=code — объявлены кодом сервиса, удалить нельзя) и свои (source=admin — созданы
/// в админке, например для фронта, который прячет кнопки по праву). В Keycloak — роли клиента приложения kind=permission.
/// </summary>
public sealed class PermissionsHandler(
    IIdentityAdmin identityAdmin,
    AppCatalog catalog,
    IValidator<CreatePermissionRequest> validator,
    AccessChanges accessChanges,
    AdminAudit audit)
{
    public async Task<Result<IReadOnlyList<PermissionResponse>, Error>> ListAsync(string clientId, CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<AppRole>, Error> roles = await AppRolesAsync(clientId, cancellationToken);
        return roles.IsFailure
            ? roles.Error
            : Result.Success<IReadOnlyList<PermissionResponse>, Error>(
                [.. roles.Value.Where(IsPermission).Select(role => new PermissionResponse(role.Name, role.Description, role.Source))]);
    }

    public async Task<Result<IReadOnlyList<PermissionResponse>, Error>> CreateAsync(
        string clientId,
        CreatePermissionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        ValidationResult validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return validation.ToError();
        }

        Result<IReadOnlyList<AppRole>, Error> roles = await AppRolesAsync(clientId, cancellationToken);
        if (roles.IsFailure)
        {
            return roles.Error;
        }

        var permission = new AppRole(null, request.Name!, request.Description?.Trim(), AppRoleKinds.PERMISSION, AppRoleKinds.SOURCE_ADMIN, false);
        UnitResult<Error> created = await identityAdmin.CreateAppRoleAsync(clientId, permission, cancellationToken);
        if (created.IsFailure)
        {
            return created.Error;
        }

        Result<IReadOnlyList<PermissionResponse>, Error> list = await ListAsync(clientId, cancellationToken);
        Result<IReadOnlyList<AppRole>, Error> after = await AppRolesAsync(clientId, cancellationToken);
        Guid id = after.IsSuccess ? after.Value.FirstOrDefault(role => string.Equals(role.Name, permission.Name, StringComparison.Ordinal))?.Id ?? Guid.Empty : Guid.Empty;
        await audit.RecordAsync(
            AuditActions.PERMISSION_CREATED, AuditActions.PERMISSION_OBJECT, id, permission.Name,
            new { app = clientId, description = permission.Description }, cancellationToken);
        return list;
    }

    public async Task<Result<IReadOnlyList<PermissionResponse>, Error>> DeleteAsync(
        string clientId,
        string name,
        CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<AppRole>, Error> roles = await AppRolesAsync(clientId, cancellationToken);
        if (roles.IsFailure)
        {
            return roles.Error;
        }

        AppRole? found = roles.Value.FirstOrDefault(role => string.Equals(role.Name, name, StringComparison.Ordinal));
        if (found is null)
        {
            return IdentityProviderErrors.RoleNotFound(name);
        }

        if (!IsPermission(found))
        {
            return AdminErrors.NotPermission(name);
        }

        if (!string.Equals(found.Source, AppRoleKinds.SOURCE_ADMIN, StringComparison.Ordinal))
        {
            return AdminErrors.SystemPermission(name);
        }

        // Роли, в составе которых право: после удаления их токены устарели (умный рефреш).
        List<AppRole> holders = [];
        foreach (AppRole role in roles.Value.Where(AppCatalog.IsRole))
        {
            Result<IReadOnlyList<string>, Error> composites = await identityAdmin.GetRoleCompositesAsync(clientId, role.Name, cancellationToken);
            if (composites.IsFailure)
            {
                return composites.Error;
            }

            if (composites.Value.Contains(name, StringComparer.Ordinal))
            {
                holders.Add(role);
            }
        }

        UnitResult<Error> deleted = await identityAdmin.DeleteAppRoleAsync(clientId, name, cancellationToken);
        if (deleted.IsFailure)
        {
            return deleted.Error;
        }

        foreach (AppRole role in holders)
        {
            UnitResult<Error> changed = await accessChanges.RoleChangedAsync(clientId, role, cancellationToken);
            if (changed.IsFailure)
            {
                return changed.Error;
            }
        }

        await audit.RecordAsync(
            AuditActions.PERMISSION_DELETED, AuditActions.PERMISSION_OBJECT, found.Id ?? Guid.Empty, found.Name,
            new { app = clientId }, cancellationToken);
        return await ListAsync(clientId, cancellationToken);
    }

    private Task<Result<IReadOnlyList<AppRole>, Error>> AppRolesAsync(string clientId, CancellationToken cancellationToken) =>
        catalog.LoadAsync(clientId, cancellationToken);

    private static bool IsPermission(AppRole role) => AppCatalog.IsPermission(role);
}

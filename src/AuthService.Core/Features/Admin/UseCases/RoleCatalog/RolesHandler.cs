using AuthService.Contracts.Admin;
using AuthService.Core.Abstractions;
using AuthService.Core.Authorization;
using AuthService.Domain.AuditLog;
using CSharpFunctionalExtensions;
using FluentValidation;
using FluentValidation.Results;
using PlatformAuth;
using SharedKernel;
using Validation;

namespace AuthService.Core.Features.Admin.UseCases.RoleCatalog;

/// <summary>
/// Роли приложения (они же режимы) и их права. В Keycloak — составные роли клиента приложения (kind=role); права
/// роли — её состав. Встроенные роли (customer, manager, admin) удалить нельзя; у admin нельзя забрать roles.manage.
/// </summary>
public sealed class RolesHandler(
    IIdentityAdmin identityAdmin,
    AppCatalog catalog,
    IValidator<CreateRoleRequest> createValidator,
    IValidator<UpdateRoleRequest> updateValidator,
    AccessChanges accessChanges,
    AdminAudit audit)
{
    public async Task<Result<IReadOnlyList<RoleResponse>, Error>> ListAsync(string clientId, CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<AppRole>, Error> roles = await catalog.LoadAsync(clientId, cancellationToken);
        if (roles.IsFailure)
        {
            return roles.Error;
        }

        HashSet<string> permissions = [.. roles.Value.Where(AppCatalog.IsPermission).Select(role => role.Name)];
        List<RoleResponse> result = [];
        foreach (AppRole role in roles.Value.Where(AppCatalog.IsRole))
        {
            Result<IReadOnlyList<string>, Error> composites = await identityAdmin.GetRoleCompositesAsync(clientId, role.Name, cancellationToken);
            if (composites.IsFailure)
            {
                return composites.Error;
            }

            result.Add(new RoleResponse(role.Name, role.Description, role.Builtin, [.. composites.Value.Where(permissions.Contains)]));
        }

        return result;
    }

    public async Task<Result<IReadOnlyList<RoleResponse>, Error>> CreateAsync(
        string clientId,
        CreateRoleRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        ValidationResult validation = await createValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return validation.ToError();
        }

        Result<IReadOnlyList<AppRole>, Error> roles = await catalog.LoadAsync(clientId, cancellationToken);
        if (roles.IsFailure)
        {
            return roles.Error;
        }

        string[] wanted = [.. (request.Permissions ?? []).Distinct(StringComparer.Ordinal)];
        UnitResult<Error> known = KnownPermissions(roles.Value, wanted);
        if (known.IsFailure)
        {
            return known.Error;
        }

        var role = new AppRole(null, request.Name!, request.Description?.Trim(), AppRoleKinds.ROLE, AppRoleKinds.SOURCE_ADMIN, false);
        UnitResult<Error> created = await identityAdmin.CreateAppRoleAsync(clientId, role, cancellationToken);
        if (created.IsFailure)
        {
            return created.Error;
        }

        UnitResult<Error> composed = await identityAdmin.SetRoleCompositesAsync(clientId, role.Name, wanted, [], cancellationToken);
        if (composed.IsFailure)
        {
            return composed.Error;
        }

        // Версия с самого начала: роль с этим именем могли удалить раньше, и отметка удаления ещё лежит в Redis —
        // без версии токены нового режима считались бы устаревшими.
        UnitResult<Error> versioned = await accessChanges.RoleChangedAsync(clientId, role, cancellationToken);
        if (versioned.IsFailure)
        {
            return versioned.Error;
        }

        await RecordAsync(clientId, AuditActions.ROLE_CREATED, role.Name, new { app = clientId, permissions = wanted }, cancellationToken);
        return await ListAsync(clientId, cancellationToken);
    }

    public async Task<Result<IReadOnlyList<RoleResponse>, Error>> UpdateAsync(
        string clientId,
        string name,
        UpdateRoleRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        ValidationResult validation = await updateValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return validation.ToError();
        }

        Result<AppRole, Error> role = await FindRoleAsync(clientId, name, cancellationToken);
        if (role.IsFailure)
        {
            return role.Error;
        }

        Result<IReadOnlyList<AppRole>, Error> roles = await catalog.LoadAsync(clientId, cancellationToken);
        if (roles.IsFailure)
        {
            return roles.Error;
        }

        string[] wanted = [.. request.Permissions!.Distinct(StringComparer.Ordinal)];
        UnitResult<Error> known = KnownPermissions(roles.Value, wanted);
        if (known.IsFailure)
        {
            return known.Error;
        }

        if (string.Equals(name, AuthModes.ADMIN, StringComparison.Ordinal) && !wanted.Contains(AuthServicePermissions.Roles.MANAGE))
        {
            return AdminErrors.RolesLockout();
        }

        Result<IReadOnlyList<string>, Error> current = await identityAdmin.GetRoleCompositesAsync(clientId, name, cancellationToken);
        if (current.IsFailure)
        {
            return current.Error;
        }

        HashSet<string> permissions = [.. roles.Value.Where(AppCatalog.IsPermission).Select(found => found.Name)];
        string[] before = [.. current.Value.Where(permissions.Contains)];
        string[] add = [.. wanted.Except(before, StringComparer.Ordinal)];
        string[] remove = [.. before.Except(wanted, StringComparer.Ordinal)];
        string? description = request.Description?.Trim();
        AppRole described = role.Value with { Description = description };
        bool composeChanges = add.Length > 0 || remove.Length > 0;

        // Состав изменился — описание запишется вместе с новой версией роли.
        if (!composeChanges && !string.Equals(description, role.Value.Description, StringComparison.Ordinal))
        {
            UnitResult<Error> saved = await identityAdmin.UpdateAppRoleAsync(clientId, described, cancellationToken);
            if (saved.IsFailure)
            {
                return saved.Error;
            }
        }

        if (composeChanges)
        {
            UnitResult<Error> composed = await identityAdmin.SetRoleCompositesAsync(clientId, name, add, remove, cancellationToken);
            if (composed.IsFailure)
            {
                return composed.Error;
            }

            // Токены в этом режиме устарели: клиенты обновят их без перелогина и получат новые права.
            UnitResult<Error> changed = await accessChanges.RoleChangedAsync(clientId, described, cancellationToken);
            if (changed.IsFailure)
            {
                return changed.Error;
            }

            await RecordAsync(clientId, AuditActions.ROLE_UPDATED, name,
                new { app = clientId, permissions = new { old = before, @new = wanted.Order(StringComparer.Ordinal).ToArray() } }, cancellationToken);
        }

        return await ListAsync(clientId, cancellationToken);
    }

    public async Task<Result<IReadOnlyList<RoleResponse>, Error>> DeleteAsync(string clientId, string name, CancellationToken cancellationToken)
    {
        Result<AppRole, Error> role = await FindRoleAsync(clientId, name, cancellationToken);
        if (role.IsFailure)
        {
            return role.Error;
        }

        if (role.Value.Builtin)
        {
            return AdminErrors.BuiltinRole(name);
        }

        UnitResult<Error> deleted = await identityAdmin.DeleteAppRoleAsync(clientId, name, cancellationToken);
        if (deleted.IsFailure)
        {
            return deleted.Error;
        }

        await accessChanges.RoleDeletedAsync(clientId, name, cancellationToken);

        await RecordAsync(clientId, AuditActions.ROLE_DELETED, name, new { app = clientId }, cancellationToken, role.Value.Id);
        return await ListAsync(clientId, cancellationToken);
    }

    private async Task<Result<AppRole, Error>> FindRoleAsync(string clientId, string name, CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<AppRole>, Error> roles = await catalog.LoadAsync(clientId, cancellationToken);
        if (roles.IsFailure)
        {
            return roles.Error;
        }

        AppRole? role = roles.Value.FirstOrDefault(found => string.Equals(found.Name, name, StringComparison.Ordinal));
        if (role is null)
        {
            return IdentityProviderErrors.RoleNotFound(name);
        }

        return AppCatalog.IsRole(role) ? role : AdminErrors.NotRole(name);
    }

    private static UnitResult<Error> KnownPermissions(IReadOnlyList<AppRole> roles, IReadOnlyCollection<string> wanted)
    {
        HashSet<string> permissions = [.. roles.Where(AppCatalog.IsPermission).Select(role => role.Name)];
        string[] unknown = [.. wanted.Where(name => !permissions.Contains(name))];
        return unknown.Length == 0 ? UnitResult.Success<Error>() : AdminErrors.UnknownPermissions(unknown);
    }

    private async Task RecordAsync(
        string clientId,
        string action,
        string name,
        object changes,
        CancellationToken cancellationToken,
        Guid? knownId = null)
    {
        Guid id = knownId ?? Guid.Empty;
        if (knownId is null)
        {
            Result<IReadOnlyList<AppRole>, Error> roles = await identityAdmin.GetAppRolesAsync(clientId, cancellationToken);
            id = roles.IsSuccess ? roles.Value.FirstOrDefault(role => string.Equals(role.Name, name, StringComparison.Ordinal))?.Id ?? Guid.Empty : Guid.Empty;
        }

        await audit.RecordAsync(action, AuditActions.ROLE_OBJECT, id, name, changes, cancellationToken);
    }
}

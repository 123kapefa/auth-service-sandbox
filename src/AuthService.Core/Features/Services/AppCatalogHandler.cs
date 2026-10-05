using AuthService.Contracts.Services;
using AuthService.Core.Abstractions;
using AuthService.Core.Features.Admin;
using AuthService.Core.Features.Admin.UseCases.Permissions;
using AuthService.Core.Features.Admin.UseCases.RoleCatalog;
using AuthService.Domain.AuditLog;
using CSharpFunctionalExtensions;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PlatformAuth;
using SharedKernel;
using Validation;

namespace AuthService.Core.Features.Services;

/// <summary>
/// Каталог приложения из его кода (PUT /auth/services/catalog): права и встроенные роли, которые сервис приложения
/// объявляет при старте своим ключом. Так же сервис входа заводит свои права (PermissionCatalogSync), только здесь —
/// для чужого приложения и по сети.
/// <para>
/// Зачем: права проверяет код (RequirePermissions), значит, и объявлять их должен код — тогда право не забудут
/// завести, не опечатаются в имени и не удалят в админке, пока код его проверяет. Роли — стартовый набор: с ним
/// приложение работает сразу после установки, без ручной настройки.
/// </para>
/// Правила:
/// <list type="bullet">
/// <item>приложение — по коду ключа: ключ service-orders объявляет только приложение orders; приложение сервиса
/// входа — никто, кроме него самого;</item>
/// <item>права — главные в коде: создаются (source=code, удалить в админке нельзя), описание обновляется; право,
/// заведённое раньше в админке, становится правом из кода; права, исчезнувшие из кода, не удаляются (могут быть в
/// ролях) — только попадают в ответ как Orphans;</item>
/// <item>роли — заводятся один раз: нет роли — создаётся встроенной (удалить нельзя) с объявленным составом; есть —
/// не трогается, её составом управляет админ, и его правки следующий старт не затрёт. Исключение — пустая роль,
/// которую завёл add-app.py (роль по умолчанию без source): ей проставляется объявленный состав.</item>
/// </list>
/// Сначала проверяется весь каталог, потом пишется: ошибка в одной роли не оставляет каталог наполовину записанным.
/// </summary>
public sealed partial class AppCatalogHandler(
    IIdentityAdmin identityAdmin,
    IValidator<DeclareCatalogRequest> validator,
    AccessChanges accessChanges,
    AdminAudit audit,
    IOptions<JwtAuthenticationOptions> authentication,
    ILogger<AppCatalogHandler> logger)
{
    /// <summary>Префикс client_id ключей сервисов проекта: ключ «orders» — клиент service-orders.</summary>
    public const string KEY_PREFIX = "service-";

    public async Task<Result<DeclareCatalogResponse, Error>> DeclareAsync(
        string? serviceClientId,
        DeclareCatalogRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        ValidationResult validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return validation.ToError();
        }

        Result<string, Error> app = AppOf(serviceClientId);
        if (app.IsFailure)
        {
            return app.Error;
        }

        Result<IReadOnlyList<AppRole>, Error> existing = await identityAdmin.GetAppRolesAsync(app.Value, cancellationToken);
        if (existing.IsFailure)
        {
            return HasCode(existing.Error, IdentityProviderErrors.AppNotFound()) ? AppCatalogErrors.AppNotFound(app.Value) : existing.Error;
        }

        Dictionary<string, AppRole> byName = existing.Value.ToDictionary(role => role.Name, StringComparer.Ordinal);
        IReadOnlyList<DeclaredPermission> permissions = request.Permissions ?? [];
        IReadOnlyList<DeclaredRole> roles = request.Roles ?? [];
        UnitResult<Error> consistent = Check(byName, permissions, roles);
        if (consistent.IsFailure)
        {
            return consistent.Error;
        }

        Result<(List<string> Created, List<string> Updated), Error> writtenPermissions =
            await WritePermissionsAsync(app.Value, byName, permissions, cancellationToken);
        if (writtenPermissions.IsFailure)
        {
            return writtenPermissions.Error;
        }

        Result<(List<string> Created, List<string> Kept), Error> writtenRoles = await WriteRolesAsync(app.Value, byName, roles, cancellationToken);
        if (writtenRoles.IsFailure)
        {
            return writtenRoles.Error;
        }

        HashSet<string> declared = [.. permissions.Select(permission => permission.Name!)];
        string[] orphans =
        [
            .. existing.Value
                .Where(role => AppCatalog.IsPermission(role)
                               && string.Equals(role.Source, AppRoleKinds.SOURCE_CODE, StringComparison.Ordinal)
                               && !declared.Contains(role.Name))
                .Select(role => role.Name)
                .Order(StringComparer.Ordinal),
        ];
        LogDeclared(logger, app.Value, serviceClientId, permissions.Count, roles.Count);
        return new DeclareCatalogResponse(
            app.Value,
            writtenPermissions.Value.Created,
            writtenPermissions.Value.Updated,
            writtenRoles.Value.Created,
            writtenRoles.Value.Kept,
            orphans);
    }

    // Приложение ключа: service-{код} → {код}. Приложение сервиса входа объявляет свой каталог само, при старте.
    private Result<string, Error> AppOf(string? serviceClientId)
    {
        if (serviceClientId is null
            || !serviceClientId.StartsWith(KEY_PREFIX, StringComparison.Ordinal)
            || serviceClientId.Length == KEY_PREFIX.Length)
        {
            return AppCatalogErrors.NotAppKey(serviceClientId);
        }

        string app = serviceClientId[KEY_PREFIX.Length..];
        return string.Equals(app, authentication.Value.Audience, StringComparison.Ordinal)
            ? AppCatalogErrors.AuthServiceApp(app)
            : app;
    }

    // Весь каталог до записи: имя не занято чужим видом (право ≠ роль), права ролей известны.
    private static UnitResult<Error> Check(
        Dictionary<string, AppRole> byName,
        IReadOnlyList<DeclaredPermission> permissions,
        IReadOnlyList<DeclaredRole> roles)
    {
        foreach (DeclaredPermission permission in permissions)
        {
            if (byName.TryGetValue(permission.Name!, out AppRole? found) && !AppCatalog.IsPermission(found))
            {
                return AppCatalogErrors.NameTaken(permission.Name!, "роль");
            }
        }

        HashSet<string> known =
        [
            .. byName.Values.Where(AppCatalog.IsPermission).Select(role => role.Name),
            .. permissions.Select(permission => permission.Name!),
        ];
        foreach (DeclaredRole role in roles)
        {
            if (byName.TryGetValue(role.Name!, out AppRole? found) && AppCatalog.IsPermission(found))
            {
                return AppCatalogErrors.NameTaken(role.Name!, "право");
            }

            string[] unknown = [.. (role.Permissions ?? []).Where(name => !known.Contains(name))];
            if (unknown.Length > 0)
            {
                return AdminErrors.UnknownPermissions(unknown);
            }
        }

        return UnitResult.Success<Error>();
    }

    private async Task<Result<(List<string> Created, List<string> Updated), Error>> WritePermissionsAsync(
        string app,
        Dictionary<string, AppRole> byName,
        IReadOnlyList<DeclaredPermission> permissions,
        CancellationToken cancellationToken)
    {
        List<string> created = [];
        List<string> updated = [];
        foreach (DeclaredPermission permission in permissions)
        {
            string? description = string.IsNullOrWhiteSpace(permission.Description) ? null : permission.Description.Trim();
            var wanted = new AppRole(null, permission.Name!, description, AppRoleKinds.PERMISSION, AppRoleKinds.SOURCE_CODE, false);
            if (!byName.TryGetValue(permission.Name!, out AppRole? found))
            {
                UnitResult<Error> result = await identityAdmin.CreateAppRoleAsync(app, wanted, cancellationToken);
                if (result.IsFailure && !IsExists(result.Error))
                {
                    return result.Error; // уже есть — завёл другой экземпляр сервиса в ту же секунду: не ошибка
                }

                if (result.IsSuccess)
                {
                    created.Add(wanted.Name);
                    await audit.RecordAsync(AuditActions.PERMISSION_CREATED, AuditActions.PERMISSION_OBJECT, Guid.Empty, wanted.Name,
                        new { app, description, source = AppRoleKinds.SOURCE_CODE }, cancellationToken, app);
                }
            }
            else if (!string.Equals(found.Description, description, StringComparison.Ordinal)
                     || !string.Equals(found.Source, AppRoleKinds.SOURCE_CODE, StringComparison.Ordinal))
            {
                UnitResult<Error> result = await identityAdmin.UpdateAppRoleAsync(app, wanted, cancellationToken);
                if (result.IsFailure)
                {
                    return result.Error;
                }

                updated.Add(wanted.Name);
            }
        }

        return (created, updated);
    }

    private async Task<Result<(List<string> Created, List<string> Kept), Error>> WriteRolesAsync(
        string app,
        Dictionary<string, AppRole> byName,
        IReadOnlyList<DeclaredRole> roles,
        CancellationToken cancellationToken)
    {
        List<string> created = [];
        List<string> kept = [];
        foreach (DeclaredRole declared in roles)
        {
            string? description = string.IsNullOrWhiteSpace(declared.Description) ? null : declared.Description.Trim();
            string[] permissions = [.. (declared.Permissions ?? []).Distinct(StringComparer.Ordinal)];
            AppRole role;
            if (!byName.TryGetValue(declared.Name!, out AppRole? found))
            {
                role = new AppRole(null, declared.Name!, description, AppRoleKinds.ROLE, AppRoleKinds.SOURCE_CODE, Builtin: true);
                UnitResult<Error> result = await identityAdmin.CreateAppRoleAsync(app, role, cancellationToken);
                if (result.IsFailure)
                {
                    if (IsExists(result.Error))
                    {
                        kept.Add(role.Name); // завёл другой экземпляр сервиса — состав уже его
                        continue;
                    }

                    return result.Error;
                }
            }
            else if (found.Source is null)
            {
                // Пустая роль по умолчанию из add-app.py (ни код, ни админ её не заводили) — состав из кода.
                // add-app.py пишет в описание имя роли — тогда описание из кода.
                string? adopted = found.Description is null || string.Equals(found.Description, found.Name, StringComparison.Ordinal)
                    ? description
                    : found.Description;
                role = found with { Description = adopted, Source = AppRoleKinds.SOURCE_CODE };
            }
            else
            {
                kept.Add(found.Name); // уже заведена кодом или админом — составом управляет админ
                continue;
            }

            UnitResult<Error> composed = await identityAdmin.SetRoleCompositesAsync(app, role.Name, permissions, [], cancellationToken);
            if (composed.IsFailure)
            {
                return composed.Error;
            }

            // Версия роли — с самого начала (и пометка source=code у роли из add-app.py): токены режима обновятся.
            UnitResult<Error> versioned = await accessChanges.RoleChangedAsync(app, role, cancellationToken);
            if (versioned.IsFailure)
            {
                return versioned.Error;
            }

            created.Add(role.Name);
            await audit.RecordAsync(AuditActions.ROLE_CREATED, AuditActions.ROLE_OBJECT, found?.Id ?? Guid.Empty, role.Name,
                new { app, permissions, source = AppRoleKinds.SOURCE_CODE }, cancellationToken, app);
        }

        return (created, kept);
    }

    private static bool IsExists(Error error) => HasCode(error, IdentityProviderErrors.RoleExists(string.Empty));

    private static bool HasCode(Error error, Error expected) =>
        string.Equals(error.Messages[0].Code, expected.Messages[0].Code, StringComparison.Ordinal);

    [LoggerMessage(Level = LogLevel.Information, Message = "App catalog of {App} declared by {Service}: {Permissions} permissions, {Roles} roles")]
    private static partial void LogDeclared(ILogger logger, string app, string? service, int permissions, int roles);
}

/// <summary>
/// Каталог — не больше 200 прав и 50 ролей; имена — по тем же правилам, что в админке, без повторов; описание до 255.
/// </summary>
public sealed class DeclareCatalogRequestValidator : AbstractValidator<DeclareCatalogRequest>
{
    public const int MAX_PERMISSIONS = 200;
    public const int MAX_ROLES = 50;

    public DeclareCatalogRequestValidator()
    {
        RuleFor(request => request.Permissions)
            .Must(list => list is null || list.Count <= MAX_PERMISSIONS)
            .WithError(GeneralErrors.ValueIsInvalid("permissions"));
        RuleFor(request => request.Roles)
            .Must(list => list is null || list.Count <= MAX_ROLES)
            .WithError(GeneralErrors.ValueIsInvalid("roles"));
        RuleFor(request => request.Permissions)
            .Must(list => list is null || list.Select(permission => permission?.Name).Distinct(StringComparer.Ordinal).Count() == list.Count)
            .WithError(GeneralErrors.ValueIsInvalid("permissions"));
        RuleFor(request => request.Roles)
            .Must(list => list is null || list.Select(role => role?.Name).Distinct(StringComparer.Ordinal).Count() == list.Count)
            .WithError(GeneralErrors.ValueIsInvalid("roles"));
        RuleForEach(request => request.Permissions)
            .Must(permission => permission?.Name is { } name && System.Text.RegularExpressions.Regex.IsMatch(name, CreatePermissionRequestValidator.NAME_PATTERN)
                                && name.Length <= 100 && (permission.Description?.Length ?? 0) <= 255)
            .WithError(GeneralErrors.ValueIsInvalid("permissions"));
        RuleForEach(request => request.Roles)
            .Must(role => role?.Name is { } name && System.Text.RegularExpressions.Regex.IsMatch(name, CreateRoleRequestValidator.NAME_PATTERN)
                          && !string.Equals(name, AppRoles.ACCESS_DENIED, StringComparison.Ordinal)
                          && (role.Description?.Length ?? 0) <= 255)
            .WithError(GeneralErrors.ValueIsInvalid("roles"));
    }
}

/// <summary>Ошибки объявления каталога.</summary>
public static class AppCatalogErrors
{
    /// <summary>Ключ не service-{код}: каталог объявляет только ключ приложения.</summary>
    public static Error NotAppKey(string? clientId) =>
        Error.Authorization("catalog.not_app_key",
            $"Ключ {clientId} не относится к приложению: каталог объявляет ключ с кодом приложения (service-{{код приложения}}).");

    /// <summary>Каталог сервиса входа — только из его кода.</summary>
    public static Error AuthServiceApp(string app) =>
        Error.Authorization("catalog.auth_service_app", $"Каталог «{app}» объявляет сам сервис входа.");

    /// <summary>Приложения с кодом ключа нет в проекте.</summary>
    public static Error AppNotFound(string app) =>
        Error.NotFound("catalog.app_not_found",
            $"Приложения «{app}» нет в проекте: подключите его (scripts/project/add-app.py) — код ключа должен совпадать с кодом приложения.");

    /// <summary>Имя уже занято другим видом: право с именем роли или роль с именем права.</summary>
    public static Error NameTaken(string name, string kind) =>
        Error.Conflict("catalog.name_taken", $"«{name}» уже есть в приложении как {kind}.");
}

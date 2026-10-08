using System.Text.Json;
using AuthService.Contracts.Me;
using AuthService.Core.Abstractions;
using AuthService.Core.Features.Admin;
using AuthService.Core.Features.Admin.UseCases.AppUserRoles;
using AuthService.Core.Features.Security;
using AuthService.Domain.AuditLog;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using SharedKernel;

namespace AuthService.Core.Features.SignInMethods;

/// <summary>Кто меняет способы входа: id, когда вошёл (auth_time) и в какой сессии (sid).</summary>
public sealed record SignInMethodsActor(Guid Id, DateTimeOffset? AuthenticatedAt, string? SessionId);

/// <summary>
/// Способы входа человека (10.3): пароль, код на телефон, код на почту можно выключить — выключенный не пускает никого
/// (это делает плагин Keycloak по атрибуту disabled_sign_in_methods). Правила — SignInMethodRules; менять — только
/// после недавнего входа (5 минут). О каждом изменении — письмо или SMS человеку и запись в журнал (его видят и
/// кабинет, и админка). Админ может только вернуть способы по умолчанию.
/// </summary>
public sealed partial class SignInMethodsHandler(
    IIdentityAdmin identityAdmin,
    IProjectSignIn projectSignIn,
    IAccountNotices notices,
    IAuditLog auditLog,
    AdminAudit audit,
    AdminTargets targets,
    SessionsHandler sessions,
    TimeProvider timeProvider,
    ILogger<SignInMethodsHandler> logger)
{
    /// <summary>Сколько изменений показывать.</summary>
    public const int HISTORY_SIZE = 20;

    private static readonly string[] _historyActions =
        [AuditActions.USER_SIGN_IN_METHOD_CHANGED, AuditActions.USER_SIGN_IN_METHODS_RESET];

    public async Task<Result<SignInMethodsResponse, Error>> GetAsync(Guid userId, CancellationToken cancellationToken)
    {
        Result<Account, Error> account = await LoadAsync(userId, cancellationToken);
        if (account.IsFailure)
        {
            return account.Error;
        }

        return ToResponse(account.Value, await HistoryAsync(userId, cancellationToken));
    }

    /// <summary>Выключенный ли способ у пользователя (на самом деле, с учётом правил).</summary>
    public async Task<Result<bool, Error>> IsDisabledAsync(Guid userId, string method, CancellationToken cancellationToken)
    {
        Result<Account, Error> account = await LoadAsync(userId, cancellationToken);
        return account.Map(found => SignInMethodRules.EffectiveDisabled(found.Facts).Contains(method));
    }

    /// <summary>Человек включает или выключает свой способ входа.</summary>
    public async Task<Result<SignInMethodsResponse, Error>> SetAsync(
        SignInMethodsActor actor,
        string method,
        SetSignInMethodRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(request);

        if (!SignInMethodRules.All.Contains(method))
        {
            return SignInMethodErrors.UnknownMethod(method);
        }

        if (request.Enabled is not { } enabled)
        {
            return GeneralErrors.ValueIsRequired("enabled");
        }

        // Правило 3: украденная сессия не выключит пароль и не включит обратно утёкший способ.
        if (!RecentlyLoggedIn(actor.AuthenticatedAt))
        {
            return AdminErrors.ReauthenticationRequired();
        }

        Result<Account, Error> account = await LoadAsync(actor.Id, cancellationToken);
        if (account.IsFailure)
        {
            return account.Error;
        }

        IReadOnlySet<string> current = SignInMethodRules.EffectiveDisabled(account.Value.Facts);
        if (current.Contains(method) != enabled)
        {
            // Уже так — без журнала и уведомления. Но «включить» снимает и сохранённое выключение, которое сейчас не
            // действует (пароль выключали при ключе доступа, ключ удалили): иначе новый ключ молча выключил бы его снова.
            UnitResult<Error> forgotten = await ForgetSuspendedAsync(actor.Id, account.Value.Facts, cancellationToken);
            return forgotten.IsFailure ? forgotten.Error : await GetAsync(actor.Id, cancellationToken);
        }

        if (!enabled && SignInMethodRules.DisableBlocker(account.Value.Facts, method) is { } blocker)
        {
            return SignInMethodErrors.CannotDisable(blocker);
        }

        IEnumerable<string> next = enabled ? current.Where(item => !string.Equals(item, method, StringComparison.Ordinal)) : current.Append(method);
        UnitResult<Error> saved = await SaveAsync(actor.Id, next, cancellationToken);
        if (saved.IsFailure)
        {
            return saved.Error;
        }

        await audit.RecordAsync(
            AuditActions.USER_SIGN_IN_METHOD_CHANGED,
            account.Value.User,
            new { method, enabled = new { old = !enabled, @new = enabled } },
            cancellationToken);
        await NotifyAsync(actor.Id, new SignInMethodNotice(enabled ? SignInMethodNotice.ENABLED : SignInMethodNotice.DISABLED, method), cancellationToken);

        // Правило 6: выключили способ — выйти на других устройствах (по умолчанию да): вошедший им там больше не войдёт.
        if (!enabled && request.SignOutOtherDevices != false)
        {
            Result<Contracts.Security.TerminatedResponse, Error> signedOut =
                await sessions.TerminateOthersAsync(actor.Id, actor.SessionId, cancellationToken);
            if (signedOut.IsFailure)
            {
                LogSignOutFailed(logger, actor.Id, signedOut.Error.GetMessage());
            }
        }

        return await GetAsync(actor.Id, cancellationToken);
    }

    /// <summary>
    /// Человек отвязывает внешний вход (Яндекс ID, Госуслуги): только после недавнего входа и если останется, чем войти.
    /// Письмо или SMS, запись в журнал и по умолчанию выход на других устройствах — как при выключении способа.
    /// </summary>
    public async Task<Result<SignInMethodsResponse, Error>> UnlinkAsync(
        SignInMethodsActor actor,
        string provider,
        bool? signOutOtherDevices,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(actor);

        // Правило 3: украденная сессия не отвяжет вход, которым пользуется хозяин аккаунта.
        if (!RecentlyLoggedIn(actor.AuthenticatedAt))
        {
            return AdminErrors.ReauthenticationRequired();
        }

        Result<Account, Error> account = await LoadAsync(actor.Id, cancellationToken);
        if (account.IsFailure)
        {
            return account.Error;
        }

        if (!account.Value.LinkedProviders.Contains(provider, StringComparer.Ordinal))
        {
            return SignInMethodErrors.NotLinked(provider);
        }

        if (UnlinkBlocker(account.Value, provider) is { } blocker)
        {
            return SignInMethodErrors.CannotUnlink(blocker);
        }

        UnitResult<Error> unlinked = await identityAdmin.UnlinkProviderAsync(actor.Id, provider, cancellationToken);
        if (unlinked.IsFailure)
        {
            return unlinked.Error;
        }

        await audit.RecordAsync(AuditActions.USER_EXTERNAL_LOGIN_UNLINKED, account.Value.User, new { provider }, cancellationToken);
        await NotifyAsync(actor.Id, new SignInMethodNotice(SignInMethodNotice.UNLINKED, provider), cancellationToken);

        // Правило 6: если чужой вошёл через этот Яндекс ID или Госуслуги, его сессия закроется.
        if (signOutOtherDevices != false)
        {
            Result<Contracts.Security.TerminatedResponse, Error> signedOut =
                await sessions.TerminateOthersAsync(actor.Id, actor.SessionId, cancellationToken);
            if (signedOut.IsFailure)
            {
                LogSignOutFailed(logger, actor.Id, signedOut.Error.GetMessage());
            }
        }

        return await GetAsync(actor.Id, cancellationToken);
    }

    /// <summary>Админ возвращает способы входа по умолчанию (всё включено) — с журналом и уведомлением человеку.</summary>
    public async Task<Result<SignInMethodsResponse, Error>> ResetAsync(RoleChangeActor admin, Guid userId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(admin);

        if (admin.Id == userId)
        {
            return AdminErrors.SelfCredentials();
        }

        Result<IdentityUser, Error> user = await targets.FindAsync(userId, onlyCustomers: false, cancellationToken);
        if (user.IsFailure)
        {
            return user.Error;
        }

        // Это снимает защиту, которую человек включил сам, — как отключение 2FA, только после недавнего входа.
        if (!RecentlyLoggedIn(admin.AuthenticatedAt))
        {
            return AdminErrors.ReauthenticationRequired();
        }

        IReadOnlySet<string> disabled = SignInMethodRules.Parse(user.Value.DisabledSignInMethods);
        if (disabled.Count > 0) // всё и так включено — без записи и уведомления
        {
            UnitResult<Error> saved = await SaveAsync(userId, [], cancellationToken);
            if (saved.IsFailure)
            {
                return saved.Error;
            }

            await audit.RecordAsync(
                AuditActions.USER_SIGN_IN_METHODS_RESET,
                user.Value,
                new { disabled = SignInMethodRules.All.Where(disabled.Contains).ToArray() },
                cancellationToken);
            await NotifyAsync(userId, new SignInMethodNotice(SignInMethodNotice.RESET, null), cancellationToken);
        }

        return await GetAsync(userId, cancellationToken);
    }

    /// <summary>
    /// Забыть сохранённые выключения, которые сейчас не действуют (страховка: пароль выключали при ключе доступа или
    /// приложении-аутентификаторе, а их больше нет). Без этого они молча вернутся с новым ключом. Ничего такого — без
    /// записи. Для мест, где опора могла пропасть: админ удалил 2FA, человек удалил учётные данные, ссылка на новый пароль.
    /// </summary>
    public async Task<UnitResult<Error>> ForgetSuspendedAsync(Guid userId, CancellationToken cancellationToken)
    {
        Result<Account, Error> account = await LoadAsync(userId, cancellationToken);
        return account.IsFailure ? account.Error : await ForgetSuspendedAsync(userId, account.Value.Facts, cancellationToken);
    }

    /// <summary>
    /// То же после возврата со страницы входа (человек удалил свои учётные данные): «по возможности» — сбой уходит в лог
    /// и не мешает войти.
    /// </summary>
    public async Task ForgetSuspendedAfterLoginAsync(string accessToken, CancellationToken cancellationToken)
    {
        Guid userId;
        try
        {
            if (!Guid.TryParse(new JsonWebTokenHandler().ReadJsonWebToken(accessToken).Subject, out userId))
            {
                return;
            }
        }
        catch (ArgumentException)
        {
            return; // не JWT
        }

        try
        {
            UnitResult<Error> forgotten = await ForgetSuspendedAsync(userId, cancellationToken);
            if (forgotten.IsFailure)
            {
                LogForgetFailed(logger, userId, forgotten.Error.GetMessage());
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogForgetFailed(logger, userId, exception.Message);
        }
    }

    private async Task<UnitResult<Error>> ForgetSuspendedAsync(Guid userId, SignInFacts facts, CancellationToken cancellationToken)
    {
        IReadOnlySet<string> effective = SignInMethodRules.EffectiveDisabled(facts);
        if (facts.Disabled.All(effective.Contains))
        {
            return UnitResult.Success<Error>();
        }

        return await SaveAsync(userId, effective, cancellationToken);
    }

    private bool RecentlyLoggedIn(DateTimeOffset? authenticatedAt) =>
        authenticatedAt is { } loggedIn && timeProvider.GetUtcNow() - loggedIn <= UserRolesHandler.RecentLogin;

    private Task<UnitResult<Error>> SaveAsync(Guid userId, IEnumerable<string> disabled, CancellationToken cancellationToken) =>
        identityAdmin.UpdateUserAsync(
            userId,
            new UserChanges(null, new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [UserAttributes.DISABLED_SIGN_IN_METHODS] = SignInMethodRules.Format(disabled),
            }),
            cancellationToken);

    // Уведомление — «по возможности»: изменение уже сделано, сбой отправки уходит в лог.
    private async Task NotifyAsync(Guid userId, SignInMethodNotice notice, CancellationToken cancellationToken)
    {
        Result<string, Error> sent = await notices.SignInMethodsChangedAsync(userId, notice, cancellationToken);
        if (sent.IsFailure)
        {
            LogNoticeFailed(logger, userId, notice.Event, sent.Error.GetMessage());
        }
    }

    private async Task<Result<Account, Error>> LoadAsync(Guid userId, CancellationToken cancellationToken)
    {
        Result<IdentityUser, Error> user = await identityAdmin.GetUserAsync(userId, cancellationToken);
        if (user.IsFailure)
        {
            return user.Error;
        }

        Result<IReadOnlyList<UserCredential>, Error> credentials = await identityAdmin.GetCredentialsAsync(userId, cancellationToken);
        if (credentials.IsFailure)
        {
            return credentials.Error;
        }

        Result<IReadOnlyList<string>, Error> linked = await identityAdmin.GetLinkedProvidersAsync(userId, cancellationToken);
        if (linked.IsFailure)
        {
            return linked.Error;
        }

        IReadOnlyList<string> projectDisabled = await ProjectDisabledAsync(cancellationToken);
        HashSet<string> types = [.. credentials.Value.Select(credential => credential.Type)];
        IdentityUser found = user.Value;
        var facts = new SignInFacts(
            types.Contains(Credentials.PASSWORD),
            IsPhoneLogin(found.Username),
            !string.IsNullOrWhiteSpace(found.Email),
            types.Contains(Credentials.PASSKEY) && !projectDisabled.Contains(ProjectSignInRules.PASSKEY),
            types.Contains(Credentials.OTP),
            types.Contains(Credentials.RECOVERY_CODES),
            linked.Value.Any(provider => !projectDisabled.Contains(provider)),
            SignInMethodRules.Parse(found.DisabledSignInMethods),
            SignInMethodRules.All.Where(method => projectDisabled.Contains(ProjectMethod(method))).ToHashSet(StringComparer.Ordinal));
        return new Account(found, facts, linked.Value, projectDisabled, types.Contains(Credentials.PASSKEY));
    }

    private static string ProjectMethod(string method) => method switch
    {
        SignInMethodRules.PASSWORD => ProjectSignInRules.PASSWORD,
        SignInMethodRules.PHONE_CODE => ProjectSignInRules.PHONE,
        _ => ProjectSignInRules.EMAIL,
    };

    // Код на телефон находит человека по логину-номеру (как в плагине): номер в профиле у входа по почте — не способ
    // входа. У входа по номеру логин — сам номер цифрами.
    private static bool IsPhoneLogin(string username) => username.Length is >= 10 and <= 15 && username.All(char.IsAsciiDigit);

    private async Task<IReadOnlyList<SignInMethodChangeResponse>> HistoryAsync(Guid userId, CancellationToken cancellationToken)
    {
        List<AuditEntry> entries = [];
        foreach (string action in _historyActions)
        {
            AuditPage page = await auditLog.SearchAsync(
                new AuditQuery(userId, null, action, null, null, 1, HISTORY_SIZE), cancellationToken);
            entries.AddRange(page.Items);
        }

        return [.. entries.OrderByDescending(entry => entry.OccurredAt).Take(HISTORY_SIZE).Select(entry => ToChange(entry, userId))];
    }

    private static SignInMethodChangeResponse ToChange(AuditEntry entry, Guid userId)
    {
        string? method = null;
        bool? enabled = null;
        if (string.Equals(entry.Action, AuditActions.USER_SIGN_IN_METHOD_CHANGED, StringComparison.Ordinal))
        {
            try
            {
                using JsonDocument changes = JsonDocument.Parse(entry.Changes);
                method = changes.RootElement.TryGetProperty("method", out JsonElement name) ? name.GetString() : null;
                enabled = changes.RootElement.TryGetProperty("enabled", out JsonElement state)
                    && state.TryGetProperty("new", out JsonElement value)
                    && value.ValueKind is JsonValueKind.True or JsonValueKind.False
                        ? value.GetBoolean()
                        : null;
            }
            catch (JsonException)
            {
                // запись не того вида — без подробностей
            }
        }

        return new SignInMethodChangeResponse(entry.OccurredAt, method, enabled, entry.ActorId != userId);
    }

    // Что выключено в проекте — «по возможности»: без плагина способов входа кабинет показывает всё, как до 10.7.
    private async Task<IReadOnlyList<string>> ProjectDisabledAsync(CancellationToken cancellationToken)
    {
        Result<ProjectSignInSettings, Error> settings = await projectSignIn.GetAsync(cancellationToken);
        if (settings.IsFailure)
        {
            LogProjectSignInFailed(logger, settings.Error.GetMessage());
            return [];
        }

        DateOnly today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        return
        [
            .. ProjectSignInRules.Methods.Where(method =>
                settings.Value.Methods.TryGetValue(method, out ProjectSignInMethod? state)
                && (string.Equals(state.Status, ProjectSignInRules.OFF, StringComparison.Ordinal)
                    || (string.Equals(state.Status, ProjectSignInRules.TRANSITION, StringComparison.Ordinal) && state.Until < today))),
        ];
    }

    // Другой внешний вход в счёт, только если он не выключен в проекте (как HasExternal в фактах).
    private static string? UnlinkBlocker(Account account, string provider) =>
        SignInMethodRules.UnlinkBlocker(
            account.Facts,
            account.LinkedProviders.Any(other =>
                !string.Equals(other, provider, StringComparison.Ordinal) && !account.ProjectDisabled.Contains(other)));

    private static SignInMethodsResponse ToResponse(Account account, IReadOnlyList<SignInMethodChangeResponse> history)
    {
        Dictionary<string, string> unlinkBlockers = new(StringComparer.Ordinal);
        foreach (string provider in account.LinkedProviders)
        {
            if (UnlinkBlocker(account, provider) is { } blocker)
            {
                unlinkBlockers[provider] = blocker;
            }
        }

        IReadOnlySet<string> disabled = SignInMethodRules.EffectiveDisabled(account.Facts);
        return new SignInMethodsResponse(
            [
                .. SignInMethodRules.All.Select(method => new SignInMethodResponse(
                    method,
                    SignInMethodRules.Available(account.Facts, method),
                    !disabled.Contains(method),
                    SignInMethodRules.DisableBlocker(account.Facts, method))),
            ],
            account.HasPasskeyCredential,
            account.Facts.HasAuthenticator,
            account.Facts.HasRecoveryCodes,
            account.LinkedProviders,
            unlinkBlockers,
            history,
            account.ProjectDisabled);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Sign-in methods notice {Event} for user {UserId} was not sent: {Error}")]
    private static partial void LogNoticeFailed(ILogger logger, Guid userId, string @event, string error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Project sign-in methods were not read for the account page: {Error}")]
    private static partial void LogProjectSignInFailed(ILogger logger, string error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Other sessions of user {UserId} were not ended after a sign-in method change: {Error}")]
    private static partial void LogSignOutFailed(ILogger logger, Guid userId, string error);

    private sealed record Account(
        IdentityUser User,
        SignInFacts Facts,
        IReadOnlyList<string> LinkedProviders,
        IReadOnlyList<string> ProjectDisabled,
        bool HasPasskeyCredential);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Suspended sign-in methods of user {UserId} were not cleared: {Error}")]
    private static partial void LogForgetFailed(ILogger logger, Guid userId, string error);
}

namespace AuthService.Contracts.Me;

/// <summary>Способы входа человека: какие есть, какие включены и что можно выключить; история изменений.</summary>
/// <param name="Methods">Пароль, код на телефон, код на почту — всегда все три, по порядку.</param>
/// <param name="HasPasskey">Есть ключ доступа (вход без пароля и кода).</param>
/// <param name="HasAuthenticator">Есть приложение-аутентификатор (2FA).</param>
/// <param name="HasRecoveryCodes">Созданы коды восстановления.</param>
/// <param name="LinkedProviders">Привязанные внешние входы: yandex, esia.</param>
/// <param name="History">Изменения способов входа, новые сверху (до 20).</param>
/// <param name="ProjectDisabled">
/// Способы, выключенные в проекте (10.7): phone, email, password, passkey, yandex, esia. Кабинет их не показывает.
/// Переходный период — ещё не выключен.
/// </param>
public sealed record SignInMethodsResponse(
    IReadOnlyList<SignInMethodResponse> Methods,
    bool HasPasskey,
    bool HasAuthenticator,
    bool HasRecoveryCodes,
    IReadOnlyList<string> LinkedProviders,
    IReadOnlyList<SignInMethodChangeResponse> History,
    IReadOnlyList<string> ProjectDisabled);

/// <summary>Один способ входа.</summary>
/// <param name="Method">password, phone-code или email-code.</param>
/// <param name="Available">Есть у человека: пароль задан, телефон или почта указаны.</param>
/// <param name="Enabled">Работает (не выключен).</param>
/// <param name="DisableBlocker">
/// Почему выключить нельзя (null — можно или уже выключен): not_available — способа нет; needs_passkey_or_authenticator —
/// сначала добавьте ключ доступа или приложение-аутентификатор; last_method — это последний способ входа;
/// recovery_codes_required — сначала создайте коды восстановления.
/// </param>
public sealed record SignInMethodResponse(string Method, bool Available, bool Enabled, string? DisableBlocker);

/// <summary>Изменение способов входа.</summary>
/// <param name="At">Когда.</param>
/// <param name="Method">Какой способ; у «вернуть по умолчанию» — null.</param>
/// <param name="Enabled">Включён (true) или выключен (false); у «вернуть по умолчанию» — null.</param>
/// <param name="ByAdmin">Сделал администратор, а не сам человек.</param>
public sealed record SignInMethodChangeResponse(DateTimeOffset At, string? Method, bool? Enabled, bool ByAdmin);

/// <summary>Включить или выключить способ входа.</summary>
/// <param name="Enabled">true — включить, false — выключить.</param>
/// <param name="SignOutOtherDevices">Выйти на других устройствах (по умолчанию — да).</param>
public sealed record SetSignInMethodRequest(bool? Enabled, bool? SignOutOtherDevices = null);

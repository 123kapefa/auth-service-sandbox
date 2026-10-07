namespace AuthService.Contracts.Admin;

/// <summary>
/// Способы входа проекта — GET /auth/admin/sign-in/methods: что вообще доступно людям проекта и обязательна ли 2FA.
/// Способы: phone (номер как логин и код на него), email (почта как логин и код на неё), password, passkey, yandex, esia.
/// </summary>
/// <param name="Methods">Способ → его состояние.</param>
/// <param name="TwoFactor">optional — по желанию, required — обязательна для всех.</param>
public sealed record SignInMethodsResponse(IReadOnlyDictionary<string, SignInMethodState> Methods, string TwoFactor);

/// <summary>Состояние способа.</summary>
/// <param name="Status">on, off или transition — переходный период.</param>
/// <param name="Until">Конец переходного периода, yyyy-MM-dd (включительно); после него способ выключен.</param>
/// <param name="Since">Начало переходного периода: период оставлен тем, кто был в проекте до него.</param>
/// <param name="Configured">Только у yandex и esia: настроен ли в Keycloak (без ключей проекта не включить).</param>
public sealed record SignInMethodState(string Status, string? Until = null, DateTimeOffset? Since = null, bool? Configured = null);

/// <summary>PUT /auth/admin/sign-in/methods — все способы целиком и 2FA.</summary>
public sealed record SetSignInMethodsRequest(IReadOnlyDictionary<string, SetSignInMethodState>? Methods, string? TwoFactor);

/// <summary>Новое состояние способа: on, off или transition с Until (yyyy-MM-dd).</summary>
public sealed record SetSignInMethodState(string? Status, string? Until = null);

/// <summary>
/// GET /auth/admin/sign-in/methods/{method}/affected — сколько людей входит способом и сколько только им (кого его
/// выключение запрёт; им — переходный период).
/// </summary>
public sealed record SignInMethodUsersResponse(string Method, long Users, long Only);

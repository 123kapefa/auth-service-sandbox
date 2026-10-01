namespace AuthService.Contracts.Admin;

/// <summary>Кто я в админке: права проекта (из токена) и приложения, которыми можно управлять.</summary>
/// <param name="Id">Id пользователя.</param>
/// <param name="Phone">Телефон.</param>
/// <param name="ActiveMode">Активный режим в приложении сервиса.</param>
/// <param name="Permissions">Права активного режима — уровень проекта (пользователи, блокировка, журнал…).</param>
/// <param name="Apps">Приложения под управлением; пусто — управлять ролями нечем.</param>
public sealed record AdminMeResponse(
    Guid Id,
    string? Phone,
    string? ActiveMode,
    IReadOnlyList<string> Permissions,
    IReadOnlyList<ManagedAppResponse> Apps);

/// <summary>Приложение под управлением и права на него.</summary>
/// <param name="ClientId">Клиент приложения в Keycloak.</param>
/// <param name="Name">Название.</param>
/// <param name="Permissions">roles.manage, users.roles.manage — какие из них есть у вызвавшего в этом приложении.</param>
/// <param name="ViaProject">true — права админа проекта (из токена), false — админ самого приложения.</param>
public sealed record ManagedAppResponse(string ClientId, string? Name, IReadOnlyList<string> Permissions, bool ViaProject);

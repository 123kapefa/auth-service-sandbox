using CSharpFunctionalExtensions;
using SharedKernel;

namespace AuthService.Core.Abstractions;

/// <summary>Администрирование пользователей на сервере входа (Keycloak Admin API) глазами сервиса.</summary>
public interface IIdentityAdmin
{
    /// <summary>Пользователь по id; нет такого — NotFound.</summary>
    Task<Result<IdentityUser, Error>> GetUserAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Поиск пользователей: подстрока в логине, почте, имени (как search Admin API Keycloak), по логину по возрастанию.
    /// search пустой — все. first — сколько пропустить, max — сколько вернуть.
    /// </summary>
    Task<Result<IReadOnlyList<IdentityUser>, Error>> SearchUsersAsync(
        string? search,
        int first,
        int max,
        CancellationToken cancellationToken);

    /// <summary>
    /// Роли пользователя в клиенте (приложении), выданные ему напрямую. Роли по умолчанию realm (customer)
    /// сюда не входят — они есть у всех.
    /// </summary>
    Task<Result<IReadOnlyList<string>, Error>> GetClientRolesAsync(Guid userId, string clientId, CancellationToken cancellationToken);

    /// <summary>Задан ли пароль (пользователь мог войти только по коду).</summary>
    Task<Result<bool, Error>> HasPasswordAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Записать атрибут пользователя; остальные атрибуты не меняются.</summary>
    Task<UnitResult<Error>> SetAttributeAsync(Guid userId, string name, string value, CancellationToken cancellationToken);
}

/// <summary>Пользователь сервера входа.</summary>
/// <param name="Id">Id пользователя (sub в токене).</param>
/// <param name="Username">Логин: номер цифрами или почта.</param>
/// <param name="Email">Почта, если есть.</param>
/// <param name="Phone">Телефон с «+», если есть.</param>
/// <param name="ActiveMode">Активный режим.</param>
/// <param name="Enabled">false — заблокирован.</param>
/// <param name="CreatedAt">Когда создан.</param>
public sealed record IdentityUser(
    Guid Id,
    string Username,
    string? Email,
    string? Phone,
    string? ActiveMode,
    bool Enabled,
    DateTimeOffset CreatedAt);

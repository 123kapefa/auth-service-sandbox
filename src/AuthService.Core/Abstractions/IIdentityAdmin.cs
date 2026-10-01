using CSharpFunctionalExtensions;
using SharedKernel;

namespace AuthService.Core.Abstractions;

/// <summary>Администрирование пользователей на сервере входа (Keycloak Admin API) глазами сервиса.</summary>
public interface IIdentityAdmin
{
    /// <summary>Пользователь по id; нет такого — NotFound.</summary>
    Task<Result<IdentityUser, Error>> GetUserAsync(Guid userId, CancellationToken cancellationToken);
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

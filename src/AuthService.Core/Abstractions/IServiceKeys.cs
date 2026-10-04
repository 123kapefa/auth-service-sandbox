using AuthService.Domain.ServiceKeys;
using CSharpFunctionalExtensions;
using SharedKernel;

namespace AuthService.Core.Abstractions;

/// <summary>Ключи сервисов проекта в базе сервиса входа (auth.service_keys).</summary>
public interface IServiceKeyStore
{
    Task<IReadOnlyList<ServiceKey>> ListAsync(CancellationToken cancellationToken);

    Task<ServiceKey?> FindAsync(string code, CancellationToken cancellationToken);

    Task AddAsync(ServiceKey key, CancellationToken cancellationToken);

    /// <summary>Сохранить изменения уже прочитанного ключа.</summary>
    Task SaveAsync(ServiceKey key, CancellationToken cancellationToken);

    Task RemoveAsync(ServiceKey key, CancellationToken cancellationToken);
}

/// <summary>
/// Регистрация клиентов в Keycloak (client registration API): сервис входа выдаёт ключи по «разрешению на регистрацию»
/// (initial access token, выдаёт техник), без права управлять всеми клиентами realm. Каждое обращение возвращает новый
/// токен управления клиентом.
/// </summary>
public interface IServiceRegistry
{
    /// <summary>Выдача ключей настроена (есть разрешение на регистрацию).</summary>
    bool IsConfigured { get; }

    Task<Result<RegisteredService, Error>> RegisterAsync(string clientId, string name, CancellationToken cancellationToken);

    /// <summary>Новый секрет; старый Keycloak сразу перестаёт принимать. Новый токен управления — в ответе.</summary>
    Task<Result<RotatedSecret, Error>> RotateSecretAsync(string clientId, string registrationToken, CancellationToken cancellationToken);

    Task<UnitResult<Error>> DeleteAsync(string clientId, string registrationToken, CancellationToken cancellationToken);
}

/// <summary>Клиент в Keycloak: секрет, токен управления и учётка сервиса (sub его токенов).</summary>
public sealed record RegisteredService(string ClientSecret, string RegistrationToken, Guid ServiceAccountId);

/// <summary>Перевыпуск: новый секрет и новый токен управления (старый Keycloak больше не принимает).</summary>
public sealed record RotatedSecret(string ClientSecret, string RegistrationToken);

public static class ServiceRegistryErrors
{
    public const string MANAGEMENT_REJECTED = "services.management_rejected";

    /// <summary>Техник не выдал сервису входа разрешение на регистрацию (Keycloak:RegistrationToken).</summary>
    public static Error NotConfigured() =>
        Error.Conflict("services.registration_unavailable", "Выдача ключей не настроена: нет разрешения на регистрацию в Keycloak.");

    /// <summary>Разрешение на регистрацию истекло или исчерпано — нужно новое.</summary>
    public static Error RegistrationRejected() =>
        Error.Conflict("services.registration_rejected", "Keycloak не принял разрешение на регистрацию: оно истекло или исчерпано.");

    /// <summary>Токен управления клиентом не принят: клиента уже нет или им управляли вне сервиса входа.</summary>
    public static Error ManagementTokenRejected(string clientId) =>
        Error.Conflict(MANAGEMENT_REJECTED, $"Keycloak не принял токен управления клиентом «{clientId}»: клиента нет или его меняли вне сервиса входа.");

    /// <summary>Keycloak отклонил регистрацию (политика регистрации, неверные данные).</summary>
    public static Error RegistrationInvalid(string clientId) =>
        Error.Validation("services.registration_invalid", $"Keycloak отклонил регистрацию клиента «{clientId}» (политика регистрации).");

    /// <summary>Такой клиент в Keycloak уже есть.</summary>
    public static Error ClientExists(string clientId) =>
        Error.Conflict("services.exists", $"Клиент «{clientId}» уже есть в Keycloak.");
}

namespace AuthService.Domain.ServiceKeys;

/// <summary>
/// Ключ сервиса проекта — клиент Keycloak (client_id + секрет), который выдал админ проекта. Секрет здесь не хранится:
/// его видит только админ в момент выдачи (дальше — только перевыпустить). Хранится токен управления клиентом
/// (зашифрованный) — им сервис входа перевыпускает и отзывает ключ — и учётка сервиса в Keycloak, чтобы мгновенно
/// отозвать уже выданные ему токены.
/// </summary>
public sealed class ServiceKey
{
    public const int MAX_CODE_LENGTH = 40;
    public const int MAX_NAME_LENGTH = 100;

    /// <summary>Префикс client_id в Keycloak: сервис «orders» — клиент «service-orders».</summary>
    public const string CLIENT_ID_PREFIX = "service-";

    private ServiceKey()
    {
    }

    public Guid Id { get; private set; }

    /// <summary>Код сервиса: латиница, цифры, дефис (orders). Из него — client_id.</summary>
    public string Code { get; private set; } = string.Empty;

    public string ClientId { get; private set; } = string.Empty;

    /// <summary>Название для админки: «Заказы».</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>Учётка сервиса в Keycloak (sub его токенов) — для мгновенного отзыва.</summary>
    public Guid ServiceAccountId { get; private set; }

    /// <summary>Токен управления клиентом (Keycloak client registration), зашифрован; после каждого использования — новый.</summary>
    public string ProtectedRegistrationToken { get; private set; } = string.Empty;

    public DateTimeOffset IssuedAt { get; private set; }

    public Guid? IssuedBy { get; private set; }

    public DateTimeOffset? RotatedAt { get; private set; }

    public static string ClientIdOf(string code) => CLIENT_ID_PREFIX + code;

    public static ServiceKey Issue(
        string code,
        string name,
        Guid serviceAccountId,
        string protectedRegistrationToken,
        DateTimeOffset issuedAt,
        Guid? issuedBy) =>
        new()
        {
            Id = Guid.CreateVersion7(issuedAt),
            Code = code,
            ClientId = ClientIdOf(code),
            Name = name,
            ServiceAccountId = serviceAccountId,
            ProtectedRegistrationToken = protectedRegistrationToken,
            IssuedAt = issuedAt,
            IssuedBy = issuedBy,
        };

    /// <summary>Ключ перевыпущен: новый токен управления (старый Keycloak больше не принимает).</summary>
    public void Rotated(string protectedRegistrationToken, DateTimeOffset rotatedAt)
    {
        ProtectedRegistrationToken = protectedRegistrationToken;
        RotatedAt = rotatedAt;
    }

    /// <summary>Токен управления сменился без перевыпуска ключа (Keycloak выдаёт новый на каждое обращение).</summary>
    public void RegistrationTokenChanged(string protectedRegistrationToken) => ProtectedRegistrationToken = protectedRegistrationToken;
}

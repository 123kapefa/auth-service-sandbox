using AuthService.Domain.Sessions;

namespace AuthService.Core.Abstractions;

/// <summary>Устройства, с которых начаты сессии (таблица auth.login_devices).</summary>
public interface ILoginDeviceStore
{
    /// <summary>Записать устройство сессии; уже записанную (второй вход в той же сессии) не трогает. Старше Retention — удаляет.</summary>
    Task RecordAsync(LoginDevice device, CancellationToken cancellationToken);

    /// <summary>User-Agent по Id сессий пользователя; сессий без записи (или чужих) в ответе нет.</summary>
    Task<IReadOnlyDictionary<string, string?>> GetUserAgentsAsync(
        Guid userId,
        IReadOnlyCollection<string> sessionIds,
        CancellationToken cancellationToken);
}

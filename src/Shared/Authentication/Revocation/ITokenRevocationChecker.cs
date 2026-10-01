using System.Security.Claims;

namespace PlatformAuth.Revocation;

/// <summary>Проверка на каждом запросе: не отозван ли уже проверенный по подписи и сроку токен.</summary>
public interface ITokenRevocationChecker
{
    Task<bool> IsRevokedAsync(ClaimsPrincipal principal, CancellationToken cancellationToken);
}

using System.Security.Claims;

namespace PlatformAuth.Revocation;

/// <summary>Проверка на каждом запросе: не отозван ли уже проверенный по подписи и сроку токен и не изменился ли доступ.</summary>
public interface ITokenRevocationChecker
{
    Task<TokenStatus> CheckAsync(ClaimsPrincipal principal, CancellationToken cancellationToken);
}

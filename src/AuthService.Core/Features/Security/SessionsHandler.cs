using AuthService.Contracts.Security;
using AuthService.Core.Abstractions;
using CSharpFunctionalExtensions;
using SharedKernel;

namespace AuthService.Core.Features.Security;

/// <summary>
/// Сессии пользователя на сервере входа (Keycloak): список, завершить одну, завершить все остальные.
/// Текущая сессия — по claim sid токена. Завершённая сессия больше не обновляет токены; уже выданный access-токен
/// доживает свой срок (мгновенный отзыв — шаг 4.8).
/// </summary>
public sealed class SessionsHandler(IIdentityAdmin identityAdmin)
{
    public async Task<Result<SessionsResponse, Error>> ListAsync(Guid userId, string? currentSessionId, CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<IdentitySession>, Error> sessions = await identityAdmin.GetSessionsAsync(userId, cancellationToken);
        return sessions.IsFailure
            ? sessions.Error
            : new SessionsResponse([.. sessions.Value.Select(session => ToResponse(session, currentSessionId))]);
    }

    public async Task<Result<SessionsResponse, Error>> TerminateAsync(
        Guid userId,
        string sessionId,
        string? currentSessionId,
        CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<IdentitySession>, Error> sessions = await identityAdmin.GetSessionsAsync(userId, cancellationToken);
        if (sessions.IsFailure)
        {
            return sessions.Error;
        }

        // Только свою: чужая сессия выглядит так же, как несуществующая.
        if (!sessions.Value.Any(session => string.Equals(session.Id, sessionId, StringComparison.Ordinal)))
        {
            return SessionErrors.NotFound();
        }

        UnitResult<Error> deleted = await identityAdmin.DeleteSessionAsync(sessionId, cancellationToken);
        return deleted.IsFailure ? deleted.Error : await ListAsync(userId, currentSessionId, cancellationToken);
    }

    public async Task<Result<TerminatedResponse, Error>> TerminateOthersAsync(
        Guid userId,
        string? currentSessionId,
        CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<IdentitySession>, Error> sessions = await identityAdmin.GetSessionsAsync(userId, cancellationToken);
        if (sessions.IsFailure)
        {
            return sessions.Error;
        }

        int terminated = 0;
        foreach (IdentitySession session in sessions.Value.Where(session => !string.Equals(session.Id, currentSessionId, StringComparison.Ordinal)))
        {
            UnitResult<Error> deleted = await identityAdmin.DeleteSessionAsync(session.Id, cancellationToken);
            if (deleted.IsFailure)
            {
                return deleted.Error;
            }

            terminated++;
        }

        return new TerminatedResponse(terminated);
    }

    private static SessionResponse ToResponse(IdentitySession session, string? currentSessionId) =>
        new(
            session.Id,
            null,
            session.IpAddress,
            null,
            session.StartedAt,
            session.LastAccessAt,
            string.Equals(session.Id, currentSessionId, StringComparison.Ordinal),
            session.Apps);
}

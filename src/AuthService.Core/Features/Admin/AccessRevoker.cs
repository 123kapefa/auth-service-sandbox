using AuthService.Core.Abstractions;
using CSharpFunctionalExtensions;
using PlatformAuth.Revocation;
using SharedKernel;

namespace AuthService.Core.Features.Admin;

/// <summary>
/// Завершить все сессии пользователя и сразу отозвать их токены: блокировка, смена роли, «выйти везде».
/// Сессии запоминаются до завершения и получают отметки по sid — их токены отклоняются независимо от секунды выдачи,
/// а новый вход (новая сессия) не задевается. Отметка по времени на пользователя — для токенов без sid.
/// </summary>
public sealed class AccessRevoker(IIdentityAdmin identityAdmin, ITokenRevocationStore revocations, TimeProvider timeProvider)
{
    public async Task<UnitResult<Error>> EndAllSessionsAsync(Guid userId, CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<IdentitySession>, Error> sessions = await identityAdmin.GetSessionsAsync(userId, cancellationToken);
        if (sessions.IsFailure)
        {
            return sessions.Error;
        }

        UnitResult<Error> loggedOut = await identityAdmin.LogoutUserAsync(userId, cancellationToken);
        if (loggedOut.IsFailure)
        {
            return loggedOut;
        }

        await revocations.RevokeUserAsync(userId.ToString(), timeProvider.GetUtcNow(), cancellationToken);
        foreach (IdentitySession session in sessions.Value)
        {
            await revocations.RevokeSessionAsync(session.Id, cancellationToken);
        }

        return UnitResult.Success<Error>();
    }
}

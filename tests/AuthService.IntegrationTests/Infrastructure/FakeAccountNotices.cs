using System.Collections.ObjectModel;
using AuthService.Core.Abstractions;
using CSharpFunctionalExtensions;
using SharedKernel;

namespace AuthService.IntegrationTests.Infrastructure;

/// <summary>Уведомления в память вместо плагина Keycloak platform-account-notice. Failure — что вернёт отправка.</summary>
public sealed class FakeAccountNotices : IAccountNotices
{
    /// <summary>Отправленные уведомления: (кому, что).</summary>
    public Collection<(Guid UserId, SignInMethodNotice Notice)> Sent { get; } = [];

    public Error? Failure { get; set; }

    public void Reset()
    {
        Sent.Clear();
        Failure = null;
    }

    public Task<Result<string, Error>> SignInMethodsChangedAsync(Guid userId, SignInMethodNotice notice, CancellationToken cancellationToken)
    {
        if (Failure is not null)
        {
            return Task.FromResult(Result.Failure<string, Error>(Failure));
        }

        Sent.Add((userId, notice));
        return Task.FromResult(Result.Success<string, Error>("email"));
    }
}

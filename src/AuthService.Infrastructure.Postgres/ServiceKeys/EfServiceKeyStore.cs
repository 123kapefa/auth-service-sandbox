using AuthService.Core.Abstractions;
using AuthService.Domain.ServiceKeys;
using Microsoft.EntityFrameworkCore;

namespace AuthService.Infrastructure.Postgres.ServiceKeys;

/// <summary>Ключи сервисов в таблице auth.service_keys.</summary>
internal sealed class EfServiceKeyStore(AuthDbContext db) : IServiceKeyStore
{
    public async Task<IReadOnlyList<ServiceKey>> ListAsync(CancellationToken cancellationToken) =>
        await db.ServiceKeys.AsNoTracking().OrderBy(key => key.Code).ToListAsync(cancellationToken);

    public Task<ServiceKey?> FindAsync(string code, CancellationToken cancellationToken) =>
        db.ServiceKeys.FirstOrDefaultAsync(key => key.Code == code, cancellationToken);

    public async Task AddAsync(ServiceKey key, CancellationToken cancellationToken)
    {
        db.ServiceKeys.Add(key);
        await db.SaveChangesAsync(cancellationToken);
    }

    public Task SaveAsync(ServiceKey key, CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);

    public async Task RemoveAsync(ServiceKey key, CancellationToken cancellationToken)
    {
        db.ServiceKeys.Remove(key);
        await db.SaveChangesAsync(cancellationToken);
    }
}

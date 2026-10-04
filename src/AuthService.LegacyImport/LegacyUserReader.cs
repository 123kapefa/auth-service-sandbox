using System.Runtime.CompilerServices;
using Npgsql;

namespace AuthService.LegacyImport;

/// <summary>Читает пользователей боевой базы потоком, по порядку создания (повторный прогон идёт в том же порядке).</summary>
public sealed class LegacyUserReader(NpgsqlDataSource dataSource)
{
    private const string SQL = """
        SELECT "Id", "Phone", "Role", "IsRestricted", "RestrictionReason", "RestrictedAtUtc", "LastLoginAtUtc",
               "LoginCount", "PasswordHash", "PasswordSetAtUtc", "CreatedAt"
        FROM users
        ORDER BY "CreatedAt", "Id"
        """;

    public async IAsyncEnumerable<LegacyUser> ReadAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = dataSource.CreateCommand(SQL);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            yield return new LegacyUser(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetBoolean(3),
                await reader.IsDBNullAsync(4, cancellationToken) ? null : reader.GetString(4),
                await NullableTimeAsync(reader, 5, cancellationToken),
                await NullableTimeAsync(reader, 6, cancellationToken),
                reader.GetInt32(7),
                await reader.IsDBNullAsync(8, cancellationToken) ? null : reader.GetString(8),
                await NullableTimeAsync(reader, 9, cancellationToken),
                await reader.GetFieldValueAsync<DateTimeOffset>(10, cancellationToken));
        }
    }

    private static async Task<DateTimeOffset?> NullableTimeAsync(NpgsqlDataReader reader, int ordinal, CancellationToken cancellationToken) =>
        await reader.IsDBNullAsync(ordinal, cancellationToken) ? null : await reader.GetFieldValueAsync<DateTimeOffset>(ordinal, cancellationToken);
}

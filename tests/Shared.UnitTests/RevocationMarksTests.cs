using PlatformAuth.Revocation;
using Xunit;

namespace Shared.UnitTests;

public sealed class RevocationMarksTests
{
    private const long MARK = 1_790_000_000;

    [Fact]
    public void NoMarks_NothingRevoked() => Assert.False(RevocationMarks.None.Revokes(MARK));

    [Theory]
    [InlineData(MARK - 1, true)]   // выдан до блокировки
    [InlineData(MARK, false)]      // в ту же секунду — действует: старые сессии закрыты отметками по sid, новый вход — нет
    [InlineData(MARK + 60, false)] // выдан после — действует
    public void User_TokensIssuedBeforeMark(long issuedAt, bool revoked) =>
        Assert.Equal(revoked, new RevocationMarks(MARK, false, null).Revokes(issuedAt));

    [Theory]
    [InlineData(MARK - 1, true)]
    [InlineData(MARK, true)]        // вся секунда отметки: войти в закрытое приложение в ту же секунду всё равно нельзя
    [InlineData(MARK + 1, false)]
    public void App_TokensIssuedUpToMark(long issuedAt, bool revoked) =>
        Assert.Equal(revoked, new RevocationMarks(null, false, MARK).Revokes(issuedAt));

    [Fact]
    public void Session_AllItsTokens() => Assert.True(new RevocationMarks(null, true, null).Revokes(MARK + 1000));
}

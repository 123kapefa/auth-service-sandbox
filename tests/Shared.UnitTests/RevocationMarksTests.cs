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

    [Theory]
    [InlineData(null, true)]        // токен без версии — старый
    [InlineData(MARK - 1, true)]
    [InlineData(MARK, false)]       // выдан уже с текущей версией
    [InlineData(MARK + 1, false)]
    public void AccessVersion_OlderTokensChanged(long? tokenVersion, bool changed) =>
        Assert.Equal(changed, new RevocationMarks(null, false, null, AccessVersion: MARK).AccessChanged(tokenVersion, null));

    [Theory]
    [InlineData(null, true)]
    [InlineData(MARK - 1, true)]
    [InlineData(MARK, false)]
    public void ModeVersion_OlderTokensChanged(long? tokenVersion, bool changed) =>
        Assert.Equal(changed, new RevocationMarks(null, false, null, ModeVersion: MARK).AccessChanged(null, tokenVersion));

    [Fact]
    public void NoVersions_NothingChanged() => Assert.False(RevocationMarks.None.AccessChanged(null, null));

    [Fact]
    public void AccessChange_IsNotRevocation() => Assert.False(new RevocationMarks(null, false, null, MARK, MARK).Revokes(0));
}

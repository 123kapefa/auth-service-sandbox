using AuthService.Core.Features.SignInMethods;
using Xunit;

namespace AuthService.UnitTests;

/// <summary>Правила способов входа — те же, что в плагине (SignInMethodsTest.java).</summary>
public sealed class SignInMethodRulesTests
{
    private const string PASSWORD = SignInMethodRules.PASSWORD;
    private const string PHONE = SignInMethodRules.PHONE_CODE;
    private const string EMAIL = SignInMethodRules.EMAIL_CODE;

    // Пароль, телефон и почта есть; остальное — по параметрам.
    private static SignInFacts Facts(
        bool passkey = false,
        bool authenticator = false,
        bool recoveryCodes = false,
        bool external = false,
        string[]? disabled = null) =>
        new(true, true, true, passkey, authenticator, recoveryCodes, external, (disabled ?? []).ToHashSet(StringComparer.Ordinal));

    [Fact]
    public void Parse_SkipsUnknown_Format_InOrder()
    {
        Assert.Equal(["email-code", "password"], SignInMethodRules.Parse(" email-code,passkey, password").Order(StringComparer.Ordinal));
        Assert.Equal("password,email-code", SignInMethodRules.Format([EMAIL, PASSWORD]));
        Assert.Null(SignInMethodRules.Format([]));
    }

    [Fact]
    public void EffectiveDisabled_PasswordAndPhone_OnlyWithPasskeyOrAuthenticator()
    {
        Assert.Equal([PASSWORD, PHONE], Sorted(SignInMethodRules.EffectiveDisabled(Facts(passkey: true, disabled: [PASSWORD, PHONE]))));
        Assert.Equal([PASSWORD, PHONE], Sorted(SignInMethodRules.EffectiveDisabled(Facts(authenticator: true, disabled: [PASSWORD, PHONE]))));
        Assert.Equal([EMAIL], Sorted(SignInMethodRules.EffectiveDisabled(Facts(disabled: [PASSWORD, PHONE, EMAIL]))));
    }

    [Fact]
    public void EffectiveDisabled_NothingLeft_NothingDisabled()
    {
        Assert.Empty(SignInMethodRules.EffectiveDisabled(Facts(authenticator: true, disabled: [PASSWORD, PHONE, EMAIL])));
        Assert.Equal(3, SignInMethodRules.EffectiveDisabled(Facts(authenticator: true, external: true, disabled: [PASSWORD, PHONE, EMAIL])).Count);
        Assert.Equal(3, SignInMethodRules.EffectiveDisabled(Facts(passkey: true, disabled: [PASSWORD, PHONE, EMAIL])).Count);
    }

    [Fact]
    public void EffectiveDisabled_MethodOffInProject_DoesNotCountAsLeft()
    {
        // Аутентификатор держит выключенные пароль и телефон, а почту проект выключил: войти нечем — не выключено ничего.
        HashSet<string> emailOff = [EMAIL];
        SignInFacts facts = Facts(authenticator: true, disabled: [PASSWORD, PHONE]) with { OffInProject = emailOff };
        SignInFacts passwordOff = Facts(authenticator: true, recoveryCodes: true, disabled: [PASSWORD]) with { OffInProject = emailOff };

        Assert.Empty(SignInMethodRules.EffectiveDisabled(facts));
        Assert.Equal(SignInMethodRules.LAST_METHOD, SignInMethodRules.DisableBlocker(passwordOff, PHONE));
    }

    [Theory]
    [InlineData(PASSWORD, SignInMethodRules.NEEDS_PASSKEY_OR_AUTHENTICATOR)]
    [InlineData(PHONE, SignInMethodRules.NEEDS_PASSKEY_OR_AUTHENTICATOR)]
    [InlineData(EMAIL, null)]
    public void DisableBlocker_NoPasskey(string method, string? blocker) =>
        Assert.Equal(blocker, SignInMethodRules.DisableBlocker(Facts(), method));

    [Fact]
    public void DisableBlocker_LastMethod()
    {
        Assert.Equal(SignInMethodRules.LAST_METHOD,
            SignInMethodRules.DisableBlocker(Facts(authenticator: true, recoveryCodes: true, disabled: [PASSWORD, PHONE]), EMAIL));
        Assert.Null(SignInMethodRules.DisableBlocker(Facts(passkey: true, disabled: [PASSWORD, PHONE]), EMAIL));
    }

    [Fact]
    public void DisableBlocker_AuthenticatorWithoutRecoveryCodes()
    {
        Assert.Equal(SignInMethodRules.RECOVERY_CODES_REQUIRED, SignInMethodRules.DisableBlocker(Facts(authenticator: true), PASSWORD));
        Assert.Null(SignInMethodRules.DisableBlocker(Facts(authenticator: true, recoveryCodes: true), PASSWORD));
        Assert.Null(SignInMethodRules.DisableBlocker(Facts(passkey: true), PASSWORD));
    }

    [Fact]
    public void DisableBlocker_NotAvailable_And_AlreadyDisabled()
    {
        SignInFacts noEmail = Facts(passkey: true) with { HasEmail = false };
        Assert.Equal(SignInMethodRules.NOT_AVAILABLE, SignInMethodRules.DisableBlocker(noEmail, EMAIL));
        Assert.Null(SignInMethodRules.DisableBlocker(Facts(passkey: true, disabled: [PASSWORD]), PASSWORD));
    }

    private static string[] Sorted(IReadOnlySet<string> methods) => [.. SignInMethodRules.All.Where(methods.Contains)];
}

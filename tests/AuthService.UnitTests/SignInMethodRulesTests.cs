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

    [Fact]
    public void UnlinkBlocker_OwnMethodWorks_CanUnlink()
    {
        Assert.Null(SignInMethodRules.UnlinkBlocker(Facts(external: true), otherExternal: false));
        Assert.Null(SignInMethodRules.UnlinkBlocker(Facts(authenticator: true, external: true, disabled: [PASSWORD, PHONE]), otherExternal: false));
    }

    [Fact]
    public void UnlinkBlocker_EverythingOwnDisabled_LastMethod_UnlessPasskeyOrOtherExternal()
    {
        // Аутентификатор держит выключенными все три способа только вместе с внешним входом — без него войти нечем.
        SignInFacts facts = Facts(authenticator: true, external: true, disabled: [PASSWORD, PHONE, EMAIL]);

        Assert.Equal(SignInMethodRules.LAST_METHOD, SignInMethodRules.UnlinkBlocker(facts, otherExternal: false));
        Assert.Null(SignInMethodRules.UnlinkBlocker(facts, otherExternal: true));
        Assert.Null(SignInMethodRules.UnlinkBlocker(facts with { HasPasskey = true }, otherExternal: false));
    }

    [Fact]
    public void UnlinkBlocker_OnlyExternal_NothingOwn_LastMethod()
    {
        // Зарегистрировался через Яндекс ID: ни пароля, ни номера-логина, ни почты.
        SignInFacts facts = new(false, false, false, false, false, false, true, new HashSet<string>(StringComparer.Ordinal));

        Assert.Equal(SignInMethodRules.LAST_METHOD, SignInMethodRules.UnlinkBlocker(facts, otherExternal: false));
    }

    [Fact]
    public void UnlinkBlocker_OwnMethodOffInProject_DoesNotCount()
    {
        SignInFacts facts = Facts(external: true) with { HasPassword = false, HasPhone = false, OffInProject = new HashSet<string>([EMAIL]) };

        Assert.Equal(SignInMethodRules.LAST_METHOD, SignInMethodRules.UnlinkBlocker(facts, otherExternal: false));
    }

    private static string[] Sorted(IReadOnlySet<string> methods) => [.. SignInMethodRules.All.Where(methods.Contains)];
}

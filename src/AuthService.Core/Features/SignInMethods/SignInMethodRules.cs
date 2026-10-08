namespace AuthService.Core.Features.SignInMethods;

/// <summary>
/// Правила способов входа (10.3), без Keycloak. Плагин (SignInMethods.java) применяет те же — держать одинаковыми:
/// выключенные пароль и код на телефон действуют, только пока есть ключ доступа или приложение-аутентификатор; если не
/// осталось ни одного способа (нет ключа доступа и внешнего входа, а всё доступное выключено) — не выключено ничего.
/// </summary>
public static class SignInMethodRules
{
    public const string PASSWORD = "password";
    public const string PHONE_CODE = "phone-code";
    public const string EMAIL_CODE = "email-code";

    /// <summary>Способы, которые человек может выключить, — в этом порядке и в ответах.</summary>
    public static IReadOnlyList<string> All { get; } = [PASSWORD, PHONE_CODE, EMAIL_CODE];

    /// <summary>Почему выключить нельзя.</summary>
    public const string NOT_AVAILABLE = "not_available";
    public const string NEEDS_PASSKEY_OR_AUTHENTICATOR = "needs_passkey_or_authenticator";
    public const string LAST_METHOD = "last_method";
    public const string RECOVERY_CODES_REQUIRED = "recovery_codes_required";

    /// <summary>Способы из атрибута; незнакомые значения пропускаются.</summary>
    public static IReadOnlySet<string> Parse(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? new HashSet<string>(StringComparer.Ordinal)
            : value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Where(All.Contains)
                .ToHashSet(StringComparer.Ordinal);

    /// <summary>Значение атрибута: способы через запятую в порядке All; пусто — null (атрибут удалить).</summary>
    public static string? Format(IEnumerable<string> disabled)
    {
        HashSet<string> set = [.. disabled];
        string value = string.Join(',', All.Where(set.Contains));
        return value.Length == 0 ? null : value;
    }

    public static bool Available(SignInFacts facts, string method)
    {
        ArgumentNullException.ThrowIfNull(facts);
        return method switch
        {
            PASSWORD => facts.HasPassword,
            PHONE_CODE => facts.HasPhone,
            EMAIL_CODE => facts.HasEmail,
            _ => false,
        };
    }

    /// <summary>Какие способы выключены на самом деле (как их применяет плагин).</summary>
    public static IReadOnlySet<string> EffectiveDisabled(SignInFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        bool anchored = facts.HasPasskey || facts.HasAuthenticator;
        HashSet<string> result = new(StringComparer.Ordinal);
        foreach (string method in All.Where(facts.Disabled.Contains))
        {
            if (anchored || string.Equals(method, EMAIL_CODE, StringComparison.Ordinal))
            {
                result.Add(method);
            }
        }

        bool nothingLeft = !facts.HasPasskey && !facts.HasExternal
            && All.Where(method => Available(facts, method) && facts.OffInProject?.Contains(method) != true).All(result.Contains);
        return nothingLeft ? new HashSet<string>(StringComparer.Ordinal) : result;
    }

    /// <summary>Почему этот способ сейчас нельзя выключить; null — можно (или он уже выключен).</summary>
    public static string? DisableBlocker(SignInFacts facts, string method)
    {
        ArgumentNullException.ThrowIfNull(facts);
        IReadOnlySet<string> current = EffectiveDisabled(facts);
        if (current.Contains(method))
        {
            return null;
        }

        if (!Available(facts, method))
        {
            return NOT_AVAILABLE;
        }

        // Правило 2: утёкший пароль или старый номер не должны открывать аккаунт без замены — ключа или 2FA.
        if (method is PASSWORD or PHONE_CODE && !facts.HasPasskey && !facts.HasAuthenticator)
        {
            return NEEDS_PASSKEY_OR_AUTHENTICATOR;
        }

        // Правило 1: после выключения должен остаться хотя бы один способ — иначе плагин не выключит ничего.
        SignInFacts next = facts with { Disabled = current.Append(method).ToHashSet(StringComparer.Ordinal) };
        if (!EffectiveDisabled(next).Contains(method))
        {
            return LAST_METHOD;
        }

        // Правило 4: коды восстановления — запасной путь, если телефон с аутентификатором потерян. Без аутентификатора
        // они не нужны: у Keycloak коды восстановления — второй шаг входа, и одни они спрашивались бы при каждом входе.
        return facts.HasAuthenticator && !facts.HasRecoveryCodes ? RECOVERY_CODES_REQUIRED : null;
    }
    /// <summary>
    /// Почему внешний вход (Яндекс ID, Госуслуги) нельзя отвязать; null — можно. Правило 1: после отвязки должно остаться,
    /// чем войти, — ключ доступа, другой внешний вход или свой способ, который работает. Иначе страховка плагина молча
    /// включила бы всё, что человек выключил.
    /// </summary>
    /// <param name="facts">Что есть у человека сейчас (с этим внешним входом).</param>
    /// <param name="otherExternal">Останется другой внешний вход, не выключенный в проекте.</param>
    public static string? UnlinkBlocker(SignInFacts facts, bool otherExternal)
    {
        ArgumentNullException.ThrowIfNull(facts);
        if (facts.HasPasskey || otherExternal)
        {
            return null;
        }

        IReadOnlySet<string> disabled = EffectiveDisabled(facts);
        bool ownLeft = All.Any(method =>
            Available(facts, method) && facts.OffInProject?.Contains(method) != true && !disabled.Contains(method));
        return ownLeft ? null : LAST_METHOD;
    }
}

/// <summary>Что есть у человека для входа.</summary>
/// <param name="HasPassword">Задан пароль.</param>
/// <param name="HasPhone">Есть телефон.</param>
/// <param name="HasEmail">Есть почта.</param>
/// <param name="HasPasskey">Есть ключ доступа, и ключи не выключены в проекте.</param>
/// <param name="HasAuthenticator">Есть приложение-аутентификатор.</param>
/// <param name="HasRecoveryCodes">Созданы коды восстановления.</param>
/// <param name="HasExternal">Привязан внешний вход, не выключенный в проекте.</param>
/// <param name="Disabled">Выключенные в атрибуте способы.</param>
/// <param name="OffInProject">Способы человека (пароль, коды), выключенные в проекте: войти ими нельзя.</param>
/// <remarks>
/// «Есть» — значит, этим можно войти (как в плагине): телефон — только у входа по номеру, ключ доступа и внешний вход,
/// выключенные в проекте, не в счёт. Иначе человек выключил бы последнее, чем на самом деле входит.
/// </remarks>
public sealed record SignInFacts(
    bool HasPassword,
    bool HasPhone,
    bool HasEmail,
    bool HasPasskey,
    bool HasAuthenticator,
    bool HasRecoveryCodes,
    bool HasExternal,
    IReadOnlySet<string> Disabled,
    IReadOnlySet<string>? OffInProject = null);

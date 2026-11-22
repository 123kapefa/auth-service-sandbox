using System.Diagnostics.Metrics;

namespace PlatformAuth;

/// <summary>
/// Метрики проверки токенов и мгновенного отзыва (встроенные <see cref="Meter"/> .NET; в Prometheus их отдаёт сервис, который
/// подключил пакет: /metrics на порту метрик). Имена в Prometheus: <c>platformauth_token_checks_total{status}</c>,
/// <c>platformauth_revocation_store_errors_total{mode,kind}</c>, <c>platformauth_revocation_writes_total{kind,result}</c>.
/// В метках только короткие перечисления: ни токенов, ни идентификаторов пользователей и сессий.
/// </summary>
public static class PlatformAuthMetrics
{
    /// <summary>Имя счётчика-источника; подписка на метрики — по этому имени.</summary>
    public const string METER_NAME = "platformauth";

    private static readonly Meter _meter = new(METER_NAME, "1.0.0");

    private static readonly Counter<long> _tokenChecks = _meter.CreateCounter<long>(
        "token_checks",
        description: "Проверки токена по отметкам отзыва: valid, revoked, access_changed, unavailable (Redis недоступен, режим «закрыто»).");

    private static readonly Counter<long> _storeErrors = _meter.CreateCounter<long>(
        "revocation_store_errors",
        description: "Ошибки хранилища отметок (Redis) при проверке: connection — недоступен или таймаут, server — отказ сервера (ACL, пароль).");

    private static readonly Counter<long> _writes = _meter.CreateCounter<long>(
        "revocation_writes",
        description: "Записи отметок отзыва: kind — user, session, app, access_changed, mode_changed; result — ok или error.");

    /// <summary>
    /// Создаёт ряды всех известных значений меток со значением 0: ряд, появившийся сразу со значением 1, Prometheus не видит как
    /// приращение, и первый сбой Redis пропал бы из <c>increase()</c> и оповещений. Вызывается при старте моста метрик.
    /// </summary>
    public static void Seed()
    {
        foreach (string status in new[] { "valid", "revoked", "access_changed", "unavailable" })
        {
            _tokenChecks.Add(0, new KeyValuePair<string, object?>("status", status));
        }

        foreach (string mode in new[] { "open", "closed" })
        {
            foreach (string kind in new[] { "connection", "server" })
            {
                _storeErrors.Add(0, new KeyValuePair<string, object?>("mode", mode), new KeyValuePair<string, object?>("kind", kind));
            }
        }

        foreach (string kind in new[] { "user", "session", "app", "access_changed", "mode_changed" })
        {
            foreach (string result in new[] { "ok", "error" })
            {
                _writes.Add(0, new KeyValuePair<string, object?>("kind", kind), new KeyValuePair<string, object?>("result", result));
            }
        }
    }

    /// <summary>Проверка токена закончилась статусом (valid, revoked, access_changed, unavailable).</summary>
    public static void TokenChecked(string status) => _tokenChecks.Add(1, new KeyValuePair<string, object?>("status", status));

    /// <summary>Хранилище отметок не ответило: mode — open или closed (режим при сбое), kind — connection или server.</summary>
    public static void StoreError(string mode, string kind) =>
        _storeErrors.Add(1, new KeyValuePair<string, object?>("mode", mode), new KeyValuePair<string, object?>("kind", kind));

    /// <summary>Запись отметки отзыва.</summary>
    public static void RevocationWritten(string kind, bool success) =>
        _writes.Add(1, new KeyValuePair<string, object?>("kind", kind), new KeyValuePair<string, object?>("result", success ? "ok" : "error"));
}

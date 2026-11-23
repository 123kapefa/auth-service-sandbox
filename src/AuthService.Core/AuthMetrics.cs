using System.Diagnostics.Metrics;

namespace AuthService.Core;

/// <summary>
/// Метрики сервиса входа (встроенные <see cref="Meter"/> .NET; в Prometheus их отдаёт порт метрик сервиса). Имена в Prometheus:
/// <c>auth_logins_started_total</c>, <c>auth_logins_total{result}</c>, <c>auth_refreshes_total{result}</c>,
/// <c>auth_logouts_total{result}</c>, <c>auth_backchannel_logouts_total{result}</c>, <c>auth_keycloak_requests_total{target,outcome}</c>,
/// <c>auth_keycloak_request_duration_seconds{target}</c>, <c>auth_rate_limited_total{policy}</c>. В метках — только короткие перечисления
/// исхода: ни адресов, ни кодов, ни идентификаторов людей и сессий. Проверка токенов и отзыв — <c>PlatformAuthMetrics</c>
/// (пакет Platform.Authentication), ответы Keycloak — здесь же (<see cref="KeycloakRequested"/>).
/// </summary>
public static class AuthMetrics
{
    public const string METER_NAME = "auth";

    private static readonly Meter _meter = new(METER_NAME, "1.0.0");

    private static readonly Counter<long> _loginsStarted = _meter.CreateCounter<long>(
        "logins_started", description: "Начат вход: браузер отправлен на страницу входа Keycloak.");

    private static readonly Counter<long> _logins = _meter.CreateCounter<long>(
        "logins",
        description: "Возврат со страницы входа: success; state_lost (метка входа потеряна или чужая — человек уходит на повторный вход); "
            + "denied (Keycloak отказал или человек отменил); invalid (нет code или state); exchange_rejected (Keycloak не принял код); "
            + "exchange_error (Keycloak недоступен или ответил ошибкой).");

    private static readonly Counter<long> _refreshes = _meter.CreateCounter<long>(
        "refreshes",
        description: "Обновление токенов: ok; no_session (нет refresh-cookie); rejected (Keycloak не принял refresh — сессия закончилась); "
            + "error (Keycloak недоступен или ответил ошибкой).");

    private static readonly Counter<long> _logouts = _meter.CreateCounter<long>(
        "logouts", description: "Выход: ok или error (Keycloak не завершил сессию; cookie удалены в любом случае).");

    private static readonly Counter<long> _backchannelLogouts = _meter.CreateCounter<long>(
        "backchannel_logouts", description: "Сообщения Keycloak о завершённой сессии: ok или rejected (не прошла проверка logout_token).");

    private static readonly Counter<long> _keycloakRequests = _meter.CreateCounter<long>(
        "keycloak_requests",
        description: "Запросы сервиса к Keycloak: target — token, logout, keys, registration, admin, other; outcome — 2xx, 3xx, 4xx, 5xx или error "
            + "(нет соединения, таймаут, отмена).");

    private static readonly Counter<long> _rateLimited = _meter.CreateCounter<long>(
        "rate_limited",
        description: "Запросы, отклонённые ограничением частоты (429): policy — login, refresh, services (по ключу сервиса), "
            + "services_anonymous (без токена сервиса, по IP).");

    private static readonly Histogram<double> _keycloakDuration = _meter.CreateHistogram<double>(
        "keycloak_request_duration_seconds", unit: "s", description: "Время ответа Keycloak на запросы сервиса.");

    /// <summary>Запрос к Keycloak закончился: куда, чем и за сколько секунд.</summary>
    public static void KeycloakRequested(string target, string outcome, double seconds)
    {
        _keycloakRequests.Add(1, new KeyValuePair<string, object?>("target", target), new KeyValuePair<string, object?>("outcome", outcome));
        _keycloakDuration.Record(seconds, new KeyValuePair<string, object?>("target", target));
    }

    /// <summary>
    /// Создаёт ряды всех известных значений меток со значением 0. Ряд, появившийся сразу со значением 1, Prometheus не видит как
    /// приращение: первый сбой (ошибка Redis, отказ Keycloak) пропал бы из <c>increase()</c> и оповещений. Вызывается при старте
    /// моста метрик.
    /// </summary>
    public static void Seed()
    {
        _loginsStarted.Add(0);
        foreach (string result in new[] { "success", "state_lost", "denied", "invalid", "exchange_rejected", "exchange_error" })
        {
            _logins.Add(0, Result(result));
        }

        foreach (string result in new[] { "ok", "no_session", "rejected", "error" })
        {
            _refreshes.Add(0, Result(result));
        }

        foreach (string result in new[] { "ok", "error" })
        {
            _logouts.Add(0, Result(result));
        }

        foreach (string result in new[] { "ok", "rejected" })
        {
            _backchannelLogouts.Add(0, Result(result));
        }

        foreach (string policy in new[] { "login", "refresh", "services", "services_anonymous" })
        {
            _rateLimited.Add(0, new KeyValuePair<string, object?>("policy", policy));
        }

        foreach (string target in new[] { "token", "logout", "keys", "registration", "admin", "other" })
        {
            foreach (string outcome in new[] { "2xx", "3xx", "4xx", "5xx", "error" })
            {
                _keycloakRequests.Add(0, new KeyValuePair<string, object?>("target", target), new KeyValuePair<string, object?>("outcome", outcome));
            }
        }
    }

    public static void RateLimited(string policy) => _rateLimited.Add(1, new KeyValuePair<string, object?>("policy", policy));

    public static void LoginStarted() => _loginsStarted.Add(1);

    public static void LoginFinished(string result) => _logins.Add(1, Result(result));

    public static void Refreshed(string result) => _refreshes.Add(1, Result(result));

    public static void LoggedOut(bool success) => _logouts.Add(1, Result(success ? "ok" : "error"));

    public static void BackchannelLoggedOut(bool success) => _backchannelLogouts.Add(1, Result(success ? "ok" : "rejected"));

    private static KeyValuePair<string, object?> Result(string result) => new("result", result);
}

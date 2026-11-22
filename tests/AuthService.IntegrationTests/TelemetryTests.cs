using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using AuthService.IntegrationTests.Infrastructure;
using Microsoft.Extensions.Logging;
using PlatformAuth.Csrf;
using Xunit;

namespace AuthService.IntegrationTests;

/// <summary>
/// 13.2: идентификатор трассы — в заголовке X-Trace-Id каждого ответа, в теле ответа с ошибкой (traceId) и в каждой записи журнала
/// запроса; код проекта — в области записи; секретов в журнале нет.
/// </summary>
public sealed partial class TelemetryTests(AuthServiceWebFactory factory) : IClassFixture<AuthServiceWebFactory>
{
    private const string TRACE_ID = "4bf92f3577b34da6a3ce929d0e0e4736";

    [GeneratedRegex("^[0-9a-f]{32}$")]
    private static partial Regex HexTraceId();

    // Начало JWT: «eyJ…» (заголовок в base64) и точка; просто «eyJ» встречается и в словах («keyJustAdded»).
    [GeneratedRegex(@"eyJ[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}")]
    private static partial Regex JwtLike();

    [Fact]
    public async Task ErrorResponse_TraceId_InHeaderAndBody_Equal()
    {
        using HttpClient browser = factory.CreateBrowserClient();

        using HttpResponseMessage response = await browser.PostAsync(new Uri("/auth/refresh", UriKind.Relative), content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        string header = Assert.Single(response.Headers.GetValues("X-Trace-Id"));
        Assert.Matches(HexTraceId(), header);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(header, body.RootElement.GetProperty("traceId").GetString());
    }

    [Fact]
    public async Task IncomingTraceparent_IsContinued_InHeaderAndBody()
    {
        using HttpClient browser = factory.CreateBrowserClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/auth/refresh", UriKind.Relative));
        request.Headers.Add("traceparent", $"00-{TRACE_ID}-00f067aa0ba902b7-01");

        using HttpResponseMessage response = await browser.SendAsync(request);

        Assert.Equal(TRACE_ID, Assert.Single(response.Headers.GetValues("X-Trace-Id")));
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(TRACE_ID, body.RootElement.GetProperty("traceId").GetString());
    }

    [Fact]
    public async Task CsrfRejection_HasTraceId()
    {
        using HttpClient browser = factory.CreateBrowserClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/auth/logout", UriKind.Relative));
        request.Headers.Add("Cookie", "accessToken=x");

        using HttpResponseMessage response = await browser.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        string header = Assert.Single(response.Headers.GetValues("X-Trace-Id"));
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(CsrfHeaderMiddleware.ERROR_CODE, body.RootElement.GetProperty("error").GetProperty("messages")[0].GetProperty("code").GetString());
        Assert.Equal(header, body.RootElement.GetProperty("traceId").GetString());
    }

    [Fact]
    public async Task Success_HasHeader_NoTraceIdInBody()
    {
        using HttpClient client = factory.CreateClientFor(TestUser.Customer);

        using HttpResponseMessage response = await client.GetAsync(new Uri("/me/", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Matches(HexTraceId(), Assert.Single(response.Headers.GetValues("X-Trace-Id")));
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(body.RootElement.TryGetProperty("traceId", out _));
    }

    [Fact]
    public async Task RequestLog_HasRouteStatusProjectAndTraceId_NoQueryNoIds()
    {
        string traceId = Guid.NewGuid().ToString("N"); // журнал общий на все тесты класса: идентификатор у каждого свой
        using HttpClient client = factory.CreateClientFor(TestUser.Customer);
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/me/?probe=secret-query-789", UriKind.Relative));
        request.Headers.Add("traceparent", $"00-{traceId}-00f067aa0ba902b7-01");

        using HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(traceId, Assert.Single(response.Headers.GetValues("X-Trace-Id")));
        CapturingLoggerProvider.LogEntry entry = Assert.Single(
            factory.Logs.Entries, e => e.Category == "AuthService.Request" && e.Scopes.Any(scope => scope.Contains(traceId, StringComparison.Ordinal)));
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.StartsWith("HTTP GET /me/ -> 200 in ", entry.Text, StringComparison.Ordinal);
        Assert.Contains(entry.Scopes, scope => scope.Contains("Project=sandbox", StringComparison.Ordinal));
        Assert.DoesNotContain("secret-query-789", entry.Everything, StringComparison.Ordinal); // строки запроса нет ни в тексте, ни в областях
    }

    [Fact]
    public async Task RequestLog_UsesRouteTemplate_NotRealIdsInThePath()
    {
        using HttpClient client = factory.CreateClientFor(TestUser.AdminInAdminMode);
        Guid id = Guid.NewGuid();
        string traceId = Guid.NewGuid().ToString("N");
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri($"/auth/admin/users/{id}", UriKind.Relative));
        request.Headers.Add("traceparent", $"00-{traceId}-00f067aa0ba902b7-01");

        using HttpResponseMessage response = await client.SendAsync(request);

        CapturingLoggerProvider.LogEntry entry = Assert.Single(
            factory.Logs.Entries, e => e.Category == "AuthService.Request" && e.Scopes.Any(scope => scope.Contains(traceId, StringComparison.Ordinal)));
        // В тексте записи — шаблон маршрута без идентификатора (путь запроса ASP.NET Core кладёт в область RequestPath сам, без строки запроса).
        Assert.Contains("/auth/admin/users/{userId", entry.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(id.ToString(), entry.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnhandledException_LoggedAsWarning_WithStatus500()
    {
        string traceId = Guid.NewGuid().ToString("N");
        factory.IdentityProvider.RefreshFault = new InvalidOperationException("boom");
        try
        {
            using HttpClient browser = factory.CreateBrowserClient();
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/auth/refresh", UriKind.Relative));
            request.Headers.Add("traceparent", $"00-{traceId}-00f067aa0ba902b7-01");
            request.Headers.Add("Cookie", "refreshToken=r");
            request.Headers.Add(CsrfHeaderMiddleware.HEADER_NAME, "XMLHttpRequest");

            // В Development исключение превращает в ответ 500 страница разработчика; в бою — Kestrel. Запись о запросе говорит правду в обоих случаях.
            using HttpResponseMessage response = await browser.SendAsync(request);
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        }
        finally
        {
            factory.IdentityProvider.RefreshFault = null;
        }

        CapturingLoggerProvider.LogEntry entry = Assert.Single(
            factory.Logs.Entries, e => e.Category == "AuthService.Request" && e.Scopes.Any(scope => scope.Contains(traceId, StringComparison.Ordinal)));
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.StartsWith("HTTP POST /auth/refresh -> 500 in ", entry.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NotFoundWithoutRoute_NotLogged_CsrfRejection_IsLoggedByTheProtection()
    {
        string notFound = Guid.NewGuid().ToString("N");
        string forbidden = Guid.NewGuid().ToString("N");
        using HttpClient browser = factory.CreateBrowserClient();
        using var missing = new HttpRequestMessage(HttpMethod.Get, new Uri("/no/such/page", UriKind.Relative));
        missing.Headers.Add("traceparent", $"00-{notFound}-00f067aa0ba902b7-01");
        using var csrf = new HttpRequestMessage(HttpMethod.Post, new Uri("/auth/logout", UriKind.Relative));
        csrf.Headers.Add("traceparent", $"00-{forbidden}-00f067aa0ba902b7-01");
        csrf.Headers.Add("Cookie", "accessToken=x");

        (await browser.SendAsync(missing)).Dispose();
        (await browser.SendAsync(csrf)).Dispose();

        Assert.DoesNotContain(factory.Logs.Entries, e => e.Category == "AuthService.Request" && e.Scopes.Any(scope => scope.Contains(notFound, StringComparison.Ordinal)));
        // Отказ защиты от CSRF — запись предупреждения самой защиты (она отвечает раньше, чем запрос доходит до журнала запросов).
        CapturingLoggerProvider.LogEntry rejected = Assert.Single(
            factory.Logs.Entries, e => e.Category.EndsWith("CsrfHeaderMiddleware", StringComparison.Ordinal) && e.Scopes.Any(scope => scope.Contains(forbidden, StringComparison.Ordinal)));
        Assert.Equal(LogLevel.Warning, rejected.Level);
        Assert.Contains("X-Requested-With", rejected.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HealthChecks_AreNotLogged()
    {
        using HttpClient browser = factory.CreateBrowserClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/health/live", UriKind.Relative));
        request.Headers.Add("traceparent", $"00-{"99998888777766665555444433332222"}-00f067aa0ba902b7-01");

        (await browser.SendAsync(request)).Dispose();

        Assert.DoesNotContain(
            factory.Logs.Entries,
            e => e.Category == "AuthService.Request" && e.Scopes.Any(scope => scope.Contains("99998888777766665555444433332222", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Logs_NeverContainSecrets_OfLoginRefreshAndLogout()
    {
        // Журнал общий на фабрику: ниже проверяется всё, что записано за прогон. Тесты идут в окружении Development, где общий уровень
        // Microsoft.AspNetCore — Information: код входа не должен попасть в журнал и так (правило для Hosting.Diagnostics).
        using HttpClient browser = factory.CreateBrowserClient();
        HttpResponseMessage login = await browser.GetAsync(new Uri("/auth/login?return_to=/me/", UriKind.Relative));
        string state = factory.IdentityProvider.LastAuthorization!.State;
        string loginCookie = login.Headers.GetValues("Set-Cookie").First(h => h.StartsWith("auth_login=", StringComparison.Ordinal)).Split(';')[0];

        using var callback = new HttpRequestMessage(HttpMethod.Get, new Uri($"/auth/callback?code=secret-code-123&state={state}", UriKind.Relative));
        callback.Headers.Add("Cookie", loginCookie);
        (await browser.SendAsync(callback)).Dispose();

        using var refresh = new HttpRequestMessage(HttpMethod.Post, new Uri("/auth/refresh", UriKind.Relative));
        refresh.Headers.Add("Cookie", "refreshToken=refresh-secret-456");
        refresh.Headers.Add(CsrfHeaderMiddleware.HEADER_NAME, "XMLHttpRequest");
        (await browser.SendAsync(refresh)).Dispose();

        string log = string.Join('\n', factory.Logs.Entries.Select(entry => entry.Everything));
        Assert.DoesNotContain("secret-code-123", log, StringComparison.Ordinal);
        Assert.DoesNotContain("refresh-secret-456", log, StringComparison.Ordinal);
        Assert.DoesNotContain(state, log, StringComparison.Ordinal);
        Assert.DoesNotContain(loginCookie.Split('=')[1], log, StringComparison.Ordinal);
        Assert.DoesNotMatch(JwtLike(), log);
    }
}

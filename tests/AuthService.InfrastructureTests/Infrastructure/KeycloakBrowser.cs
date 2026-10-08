using System.Text.Json;
using System.Text.RegularExpressions;

namespace AuthService.InfrastructureTests.Infrastructure;

/// <summary>
/// Браузер на странице входа Keycloak — как scripts/keycloak/lib.sh: cookie и ручные переходы. Страницы рисует тема
/// Keycloakify в браузере, в HTML формы нет — адрес отправки берётся из kcContext страницы (url.loginAction).
/// Cookie — свои: Keycloak ставит их с Secure, а CookieContainer не отдаёт такие по http (браузер и curl для
/// localhost отдают).
/// </summary>
public sealed partial class KeycloakBrowser : IDisposable
{
    private readonly HttpClientHandler _handler = new() { AllowAutoRedirect = false, UseCookies = false };
    private readonly Dictionary<string, string> _cookies = new(StringComparer.Ordinal);
    private readonly HttpClient _http;

    public KeycloakBrowser() => _http = new HttpClient(_handler);

    /// <summary>
    /// Открыть страницу входа и войти логином и паролем. Возвращает адрес, на который Keycloak вернул браузер
    /// (redirect_uri с code и state). Есть сессия Keycloak (SSO) — вернёт сразу, без формы.
    /// </summary>
    public async Task<Uri> SignInAsync(Uri authorizationUrl, string login, string password)
    {
        using HttpResponseMessage page = await SendAsync(new HttpRequestMessage(HttpMethod.Get, authorizationUrl));
        if (page.Headers.Location is { } redirected)
        {
            return redirected;
        }

        string html = await page.Content.ReadAsStringAsync();
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["username"] = login,
            ["password"] = password,
        });
        using HttpResponseMessage submitted = await SendAsync(new HttpRequestMessage(HttpMethod.Post, LoginAction(html)) { Content = form });
        return submitted.Headers.Location
            ?? throw new InvalidOperationException(
                $"Keycloak did not sign in ({(int)submitted.StatusCode}): {Feedback(await submitted.Content.ReadAsStringAsync())}");
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request)
    {
        using (request)
        {
            if (_cookies.Count > 0)
            {
                request.Headers.Add("Cookie", string.Join("; ", _cookies.Select(cookie => $"{cookie.Key}={cookie.Value}")));
            }

            HttpResponseMessage response = await _http.SendAsync(request);
            foreach (string header in response.Headers.TryGetValues("Set-Cookie", out IEnumerable<string>? values) ? values : [])
            {
                string[] pair = header.Split(';', 2)[0].Split('=', 2);
                bool expired = header.Contains("Max-Age=0", StringComparison.OrdinalIgnoreCase) || pair[1].Length == 0;
                if (expired)
                {
                    _cookies.Remove(pair[0]);
                }
                else
                {
                    _cookies[pair[0]] = pair[1];
                }
            }

            return response;
        }
    }

    public void Dispose()
    {
        _http.Dispose();
        _handler.Dispose();
    }

    /// <summary>Параметр из query адреса возврата (code, state, error).</summary>
    public static string? Query(Uri callback, string name) =>
        Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(callback.Query).TryGetValue(name, out var value) ? value.ToString() : null;

    private static Uri LoginAction(string html)
    {
        Match match = LoginActionPattern().Match(html);
        if (!match.Success)
        {
            throw new InvalidOperationException("No Keycloak login page (kcContext.url.loginAction): " + html[..Math.Min(html.Length, 500)]);
        }

        // Значение — строка JavaScript в кавычках: те же экранирования, что в JSON.
        return new Uri(JsonSerializer.Deserialize<string>(match.Groups["value"].Value)!);
    }

    private static string Feedback(string html)
    {
        Match match = SummaryPattern().Match(html);
        return match.Success ? JsonSerializer.Deserialize<string>(match.Groups["value"].Value)! : "no message";
    }

    [GeneratedRegex("\"loginAction\"\\s*:\\s*(?<value>\"(?:[^\"\\\\]|\\\\.)*\")")]
    private static partial Regex LoginActionPattern();

    [GeneratedRegex("\"summary\"\\s*:\\s*(?<value>\"(?:[^\"\\\\]|\\\\.)*\")")]
    private static partial Regex SummaryPattern();
}

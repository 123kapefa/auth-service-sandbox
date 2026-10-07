using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AuthService.Core.Abstractions;
using CSharpFunctionalExtensions;
using Microsoft.Extensions.Logging;
using SharedKernel;

namespace AuthService.Infrastructure.Keycloak;

/// <summary>
/// Коды подтверждения проекта через плагин Keycloak platform-codes ({realm}/platform-codes/settings, …/usage): читать и
/// менять — токеном служебной учётки с ролью realm manage-sign-in. Право manage-realm сервису входа для этого не нужно.
/// </summary>
internal sealed partial class KeycloakCodes(
    HttpClient http,
    KeycloakServiceAccount serviceAccount,
    ILogger<KeycloakCodes> logger) : IProjectCodes
{
    private const string SETTINGS = "platform-codes/settings";
    private const string USAGE = "platform-codes/usage";

    public async Task<Result<ProjectCodes, Error>> GetAsync(CancellationToken cancellationToken)
    {
        Result<SettingsRepresentation, Error> body = await SendAsync<SettingsRepresentation>(HttpMethod.Get, SETTINGS, null, cancellationToken);
        return body.Map(ToCodes);
    }

    public async Task<Result<ProjectCodes, Error>> SetAsync(ProjectCodeSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var request = new SettingsRequest(
            settings.PhoneChannels,
            settings.Countries,
            settings.DailyBudget,
            settings.TargetPerHour,
            settings.TargetPerDay,
            settings.IpPerHour,
            settings.RangePerHour,
            settings.CaptchaAfter);
        Result<SettingsRepresentation, Error> body = await SendAsync<SettingsRepresentation>(HttpMethod.Put, SETTINGS, JsonContent.Create(request), cancellationToken);
        return body.Map(ToCodes);
    }

    public async Task<Result<ProjectCodeUsage, Error>> GetUsageAsync(CancellationToken cancellationToken)
    {
        Result<UsageRepresentation, Error> body = await SendAsync<UsageRepresentation>(HttpMethod.Get, USAGE, null, cancellationToken);
        if (body.IsFailure)
        {
            return body.Error;
        }

        UsageRepresentation usage = body.Value;
        if (!DateOnly.TryParseExact(usage.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly date))
        {
            LogUnreadable(logger, null);
            return IdentityProviderErrors.Unavailable();
        }

        BudgetRepresentation budget = usage.Budget ?? new BudgetRepresentation(0, 0, 0, Exhausted: false);
        return new ProjectCodeUsage(
            date,
            usage.Zone ?? string.Empty,
            budget.Spent,
            budget.Limit,
            budget.AlertPercent,
            budget.Exhausted,
            ToCount(usage.Call),
            ToCount(usage.Sms),
            ToCount(usage.Email),
            usage.Sent,
            usage.Verified,
            usage.VerifiedPercent);
    }

    private static CodeChannelCount ToCount(ChannelRepresentation? channel) =>
        channel is null ? new CodeChannelCount(0, 0) : new CodeChannelCount(channel.Sent, channel.Cost);

    private static ProjectCodes ToCodes(SettingsRepresentation settings) =>
        new(
            new ProjectCodeSettings(
                settings.PhoneChannels ?? [],
                settings.Countries ?? [],
                settings.DailyBudget,
                settings.TargetPerHour,
                settings.TargetPerDay,
                settings.IpPerHour,
                settings.RangePerHour,
                settings.CaptchaAfter),
            new ProjectCodeServerSettings(settings.RangeDigits, settings.SmsCost, settings.CallCost, settings.BudgetAlertPercent, settings.Zone ?? string.Empty));

    private async Task<Result<T, Error>> SendAsync<T>(HttpMethod method, string path, HttpContent? content, CancellationToken cancellationToken)
        where T : class
    {
        Result<Uri, Error> realm = await serviceAccount.GetRealmBaseAsync(cancellationToken);
        if (realm.IsFailure)
        {
            return realm.Error;
        }

        Result<string, Error> token = await serviceAccount.GetTokenAsync(cancellationToken);
        if (token.IsFailure)
        {
            return token.Error;
        }

        using var request = new HttpRequestMessage(method, new Uri(realm.Value, path)) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Value);
        HttpResponseMessage message;
        try
        {
            message = await http.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            LogUnreachable(logger, exception);
            return IdentityProviderErrors.Unavailable();
        }

        using (message)
        {
            if (!message.IsSuccessStatusCode)
            {
                return await ToErrorAsync(message, cancellationToken);
            }

            try
            {
                T? body = await message.Content.ReadFromJsonAsync<T>(cancellationToken);
                return body is null ? IdentityProviderErrors.Unavailable() : body;
            }
            catch (JsonException exception)
            {
                LogUnreadable(logger, exception);
                return IdentityProviderErrors.Unavailable();
            }
        }
    }

    private async Task<Error> ToErrorAsync(HttpResponseMessage message, CancellationToken cancellationToken)
    {
        switch (message.StatusCode)
        {
            case HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden:
                LogNotPermitted(logger);
                return CodeSettingsErrors.NotPermitted();
            case HttpStatusCode.NotFound:
                LogNotInstalled(logger);
                return CodeSettingsErrors.NotInstalled();
            case HttpStatusCode.BadRequest:
                return CodeSettingsErrors.Rejected(await ReadErrorAsync(message, cancellationToken) ?? "Настройки кодов не приняты.");
            default:
                LogUnexpectedResponse(logger, (int)message.StatusCode);
                return IdentityProviderErrors.Unavailable();
        }
    }

    private static async Task<string?> ReadErrorAsync(HttpResponseMessage message, CancellationToken cancellationToken)
    {
        try
        {
            PluginError? error = await message.Content.ReadFromJsonAsync<PluginError>(cancellationToken);
            return string.IsNullOrWhiteSpace(error?.Error) ? null : error.Error;
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Keycloak is unreachable for project code settings")]
    private static partial void LogUnreachable(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Project code settings response from Keycloak is not readable")]
    private static partial void LogUnreadable(ILogger logger, Exception? exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "The service account may not read or change project code settings: grant it the realm role manage-sign-in")]
    private static partial void LogNotPermitted(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Keycloak has no platform-codes plugin: rebuild the Keycloak image")]
    private static partial void LogNotInstalled(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Unexpected project code settings response from Keycloak: {StatusCode}")]
    private static partial void LogUnexpectedResponse(ILogger logger, int statusCode);

    private sealed record SettingsRepresentation(
        [property: JsonPropertyName("phoneChannels")] IReadOnlyList<string>? PhoneChannels,
        [property: JsonPropertyName("countries")] IReadOnlyList<string>? Countries,
        [property: JsonPropertyName("dailyBudget")] long DailyBudget,
        [property: JsonPropertyName("targetPerHour")] int TargetPerHour,
        [property: JsonPropertyName("targetPerDay")] int TargetPerDay,
        [property: JsonPropertyName("ipPerHour")] int IpPerHour,
        [property: JsonPropertyName("rangePerHour")] int RangePerHour,
        [property: JsonPropertyName("captchaAfter")] int CaptchaAfter,
        [property: JsonPropertyName("rangeDigits")] int RangeDigits,
        [property: JsonPropertyName("smsCost")] long SmsCost,
        [property: JsonPropertyName("callCost")] long CallCost,
        [property: JsonPropertyName("budgetAlertPercent")] int BudgetAlertPercent,
        [property: JsonPropertyName("zone")] string? Zone);

    private sealed record SettingsRequest(
        [property: JsonPropertyName("phoneChannels")] IReadOnlyList<string> PhoneChannels,
        [property: JsonPropertyName("countries")] IReadOnlyList<string> Countries,
        [property: JsonPropertyName("dailyBudget")] long DailyBudget,
        [property: JsonPropertyName("targetPerHour")] int TargetPerHour,
        [property: JsonPropertyName("targetPerDay")] int TargetPerDay,
        [property: JsonPropertyName("ipPerHour")] int IpPerHour,
        [property: JsonPropertyName("rangePerHour")] int RangePerHour,
        [property: JsonPropertyName("captchaAfter")] int CaptchaAfter);

    private sealed record UsageRepresentation(
        [property: JsonPropertyName("date")] string? Date,
        [property: JsonPropertyName("zone")] string? Zone,
        [property: JsonPropertyName("budget")] BudgetRepresentation? Budget,
        [property: JsonPropertyName("call")] ChannelRepresentation? Call,
        [property: JsonPropertyName("sms")] ChannelRepresentation? Sms,
        [property: JsonPropertyName("email")] ChannelRepresentation? Email,
        [property: JsonPropertyName("sent")] long Sent,
        [property: JsonPropertyName("verified")] long Verified,
        [property: JsonPropertyName("verifiedPercent")] int? VerifiedPercent);

    private sealed record BudgetRepresentation(
        [property: JsonPropertyName("spent")] long Spent,
        [property: JsonPropertyName("limit")] long Limit,
        [property: JsonPropertyName("alertPercent")] int AlertPercent,
        [property: JsonPropertyName("exhausted")] bool Exhausted);

    private sealed record ChannelRepresentation(
        [property: JsonPropertyName("sent")] long Sent,
        [property: JsonPropertyName("cost")] long Cost);

    private sealed record PluginError([property: JsonPropertyName("error")] string? Error);
}

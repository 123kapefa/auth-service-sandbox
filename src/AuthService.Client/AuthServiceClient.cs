using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AuthService.Contracts.Services;
using CSharpFunctionalExtensions;
using SharedKernel;

namespace AuthService.Client;

/// <summary>
/// API сервиса входа для сервисов проекта — по ключу доступа (токен сервиса ставит ServiceTokenHandler):
/// карточка пользователя (право service.users.read) и есть ли у него право в приложении (service.access.check).
/// </summary>
public interface IAuthServiceClient
{
    Task<Result<ServiceUserResponse, Error>> GetUserAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>По логину: телефон (цифрами или +7…) или почта.</summary>
    Task<Result<ServiceUserResponse, Error>> FindUserAsync(string login, CancellationToken cancellationToken);

    /// <summary>Есть ли у пользователя право сейчас; app — client_id приложения (по умолчанию — приложение сервиса входа).</summary>
    Task<Result<AccessCheckResponse, Error>> CheckAccessAsync(Guid userId, string permission, string? app, CancellationToken cancellationToken);
}

public sealed class AuthServiceClient(HttpClient http) : IAuthServiceClient
{
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public Task<Result<ServiceUserResponse, Error>> GetUserAsync(Guid userId, CancellationToken cancellationToken) =>
        GetAsync<ServiceUserResponse>($"auth/services/users/{userId}", cancellationToken);

    public Task<Result<ServiceUserResponse, Error>> FindUserAsync(string login, CancellationToken cancellationToken) =>
        GetAsync<ServiceUserResponse>($"auth/services/users?login={Uri.EscapeDataString(login)}", cancellationToken);

    public Task<Result<AccessCheckResponse, Error>> CheckAccessAsync(
        Guid userId,
        string permission,
        string? app,
        CancellationToken cancellationToken) =>
        GetAsync<AccessCheckResponse>(
            $"auth/services/users/{userId}/access?permission={Uri.EscapeDataString(permission)}"
            + (string.IsNullOrWhiteSpace(app) ? string.Empty : $"&app={Uri.EscapeDataString(app)}"),
            cancellationToken);

    private async Task<Result<T, Error>> GetAsync<T>(string path, CancellationToken cancellationToken)
    {
        try
        {
            using HttpResponseMessage response = await http.GetAsync(new Uri(path, UriKind.Relative), cancellationToken);
            using JsonDocument body = await ReadAsync(response, cancellationToken);
            if (response.IsSuccessStatusCode && body.RootElement.TryGetProperty("result", out JsonElement result))
            {
                return result.Deserialize<T>(_json)!;
            }

            return ToError(response.StatusCode, body);
        }
        catch (HttpRequestException exception)
        {
            return Error.Failure("auth_service.unavailable", $"Сервис входа недоступен: {exception.Message}").AsTransient();
        }
    }

    private static async Task<JsonDocument> ReadAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        string text = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text);
    }

    // Ошибка сервиса — из Envelope (код и сообщение), тип — по статусу ответа.
    private static Error ToError(HttpStatusCode status, JsonDocument body)
    {
        string code = "auth_service.error";
        string message = $"Сервис входа ответил {(int)status}";
        if (body.RootElement.TryGetProperty("error", out JsonElement error)
            && error.TryGetProperty("messages", out JsonElement messages)
            && messages.GetArrayLength() > 0)
        {
            code = messages[0].GetProperty("code").GetString() ?? code;
            message = messages[0].GetProperty("message").GetString() ?? message;
        }

        return status switch
        {
            HttpStatusCode.NotFound => Error.NotFound(code, message),
            HttpStatusCode.BadRequest => Error.Validation(code, message),
            HttpStatusCode.Unauthorized => Error.Authentication(code, message),
            HttpStatusCode.Forbidden => Error.Authorization(code, message),
            HttpStatusCode.Conflict => Error.Conflict(code, message),
            _ => Error.Failure(code, message).AsTransient(),
        };
    }
}

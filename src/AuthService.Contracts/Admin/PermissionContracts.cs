namespace AuthService.Contracts.Admin;

/// <summary>Право приложения.</summary>
/// <param name="Name">Имя: users.view, reports.export…</param>
/// <param name="Description">Описание для админки.</param>
/// <param name="Source">code — объявлено кодом сервиса (удалить нельзя), admin — создано в админке.</param>
public sealed record PermissionResponse(string Name, string? Description, string? Source);

/// <summary>POST /auth/admin/apps/{clientId}/permissions — своё право: имя (через точку, латиница) и описание.</summary>
public sealed record CreatePermissionRequest(string? Name, string? Description);

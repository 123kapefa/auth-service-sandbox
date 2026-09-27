namespace AuthService.Contracts.Auth;

/// <summary>Токены обновлены (сами токены — в cookie, в тело не попадают).</summary>
public sealed record RefreshResponse(DateTimeOffset AccessTokenExpiresAt);

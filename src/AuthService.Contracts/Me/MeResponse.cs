namespace AuthService.Contracts.Me;

/// <summary>Кто я: данные из токена и права в активном режиме.</summary>
public sealed record MeResponse(
    Guid? Id,
    string? Phone,
    string? ActiveMode,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> AvailableModes,
    IReadOnlyList<string> Permissions);

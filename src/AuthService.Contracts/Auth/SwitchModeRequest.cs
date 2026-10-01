namespace AuthService.Contracts.Auth;

/// <summary>POST /auth/mode — в какой режим перейти (customer, manager, admin).</summary>
public sealed record SwitchModeRequest(string? ActiveMode);

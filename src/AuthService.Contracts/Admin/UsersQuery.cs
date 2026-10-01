namespace AuthService.Contracts.Admin;

/// <summary>GET /auth/admin/users — поиск (телефон, почта, логин) и страница (с 1, размер до 100).</summary>
public sealed record UsersQuery(string? Search, int? Page, int? PageSize);

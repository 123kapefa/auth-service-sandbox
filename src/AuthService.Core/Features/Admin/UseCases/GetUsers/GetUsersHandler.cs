using System.Text.RegularExpressions;
using AuthService.Contracts.Admin;
using AuthService.Core.Abstractions;
using AuthService.Core.Authorization;
using CSharpFunctionalExtensions;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.Extensions.Options;
using PlatformAuth;
using SharedKernel;
using Validation;

namespace AuthService.Core.Features.Admin.UseCases.GetUsers;

/// <summary>
/// Страница пользователей из Keycloak. Роль каждого — отдельным запросом ролей: Keycloak не умеет одним запросом
/// отдать пользователей с ролями клиента без прав на настройки клиентов. Менеджеру сотрудники не показываются —
/// они убираются из страницы Keycloak, поэтому строк может быть меньше pageSize, а нумерация страниц честная.
/// </summary>
public sealed partial class GetUsersHandler(
    IIdentityAdmin identityAdmin,
    AppCatalog catalog,
    IValidator<UsersQuery> validator,
    IOptions<JwtAuthenticationOptions> authentication)
{
    private const int DEFAULT_PAGE_SIZE = 20;

    public async Task<Result<UsersPageResponse, Error>> Handle(
        UsersQuery query,
        bool onlyCustomers,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        ValidationResult validation = await validator.ValidateAsync(query, cancellationToken);
        if (!validation.IsValid)
        {
            return validation.ToError();
        }

        int page = query.Page ?? 1;
        int pageSize = query.PageSize ?? DEFAULT_PAGE_SIZE;

        // На одного больше: есть ли следующая страница, без отдельного подсчёта.
        Result<IReadOnlyList<IdentityUser>, Error> found = await identityAdmin.SearchUsersAsync(
            SearchText(query.Search),
            (page - 1) * pageSize,
            pageSize + 1,
            cancellationToken);
        if (found.IsFailure)
        {
            return found.Error;
        }

        Result<IReadOnlyList<AppRole>, Error> appRoles = await catalog.LoadAsync(authentication.Value.Audience, cancellationToken);
        if (appRoles.IsFailure)
        {
            return appRoles.Error;
        }

        IdentityUser[] users = [.. found.Value.Take(pageSize)];
        Result<IReadOnlyList<string>, Error>[] roles = await Task.WhenAll(users.Select(user =>
            identityAdmin.GetClientRolesAsync(user.Id, authentication.Value.Audience, cancellationToken)));
        if (roles.FirstOrDefault(result => result.IsFailure) is { IsFailure: true } failed)
        {
            return failed.Error;
        }

        UserListItemResponse[] items =
        [
            .. users
                .Select((user, index) => (User: user, Staff: StaffRoles.Of(roles[index].Value, appRoles.Value)))
                .Where(row => !onlyCustomers || row.Staff.Count == 0)
                .Select(row => new UserListItemResponse(
                    row.User.Id,
                    row.User.Username,
                    row.User.Phone,
                    row.User.Email,
                    StaffRoles.Describe(row.Staff),
                    row.User.CreatedAt,
                    !row.User.Enabled,
                    row.User.RestrictionReason,
                    row.User.RestrictedAt)),
        ];

        return new UsersPageResponse(items, page, pageSize, found.Value.Count > pageSize);
    }

    // Логин-телефон хранится цифрами: «+7 (999) 000» ищем как «7999000». Почту и прочее — как ввели.
    internal static string? SearchText(string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return null;
        }

        string trimmed = search.Trim();
        return PhoneLike().IsMatch(trimmed) ? NonDigits().Replace(trimmed, string.Empty) : trimmed;
    }

    [GeneratedRegex(@"^[+\d\s()\-]*\d[+\d\s()\-]*$")]
    private static partial Regex PhoneLike();

    [GeneratedRegex(@"\D")]
    private static partial Regex NonDigits();
}

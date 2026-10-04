using AuthService.Contracts.Admin;
using AuthService.Core.Abstractions;
using AuthService.Core.Authorization;
using AuthService.Core.Features.Admin.UseCases.GetUsers;
using CSharpFunctionalExtensions;
using FluentValidation;
using FluentValidation.Results;
using SharedKernel;
using Validation;

namespace AuthService.Core.Features.Admin.UseCases.AppUsers;

/// <summary>
/// Пользователи приложения — для его админа: без поиска — те, у кого есть роль в приложении (выданная, не по
/// умолчанию); с поиском — один человек по точному телефону или почте, чтобы выдать роль новому. Перебрать всю базу
/// проекта так нельзя. Только логин, телефон, почта и роли: блокировка, сессии и прочее — дело админа проекта.
/// </summary>
public sealed class AppUsersHandler(IIdentityAdmin identityAdmin, AppCatalog catalog, IValidator<UsersQuery> validator)
{
    private const int DEFAULT_PAGE_SIZE = 20;

    /// <summary>Сколько держателей одной роли читается из Keycloak (страницы — в памяти).</summary>
    private const int ROLE_USERS_LIMIT = 1000;

    public async Task<Result<AppUsersPageResponse, Error>> Handle(string clientId, UsersQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        ValidationResult validation = await validator.ValidateAsync(query, cancellationToken);
        if (!validation.IsValid)
        {
            return validation.ToError();
        }

        Result<IReadOnlyList<AppRole>, Error> appRoles = await catalog.LoadAsync(clientId, cancellationToken);
        if (appRoles.IsFailure)
        {
            return appRoles.Error;
        }

        int page = query.Page ?? 1;
        int pageSize = query.PageSize ?? DEFAULT_PAGE_SIZE;
        Result<IReadOnlyList<IdentityUser>, Error> users = string.IsNullOrWhiteSpace(query.Search)
            ? await WithRolesAsync(clientId, appRoles.Value, cancellationToken)
            : await ExactAsync(query.Search, cancellationToken);
        if (users.IsFailure)
        {
            return users.Error;
        }

        IdentityUser[] slice = [.. users.Value.Skip((page - 1) * pageSize).Take(pageSize)];
        Result<IReadOnlyList<string>, Error>[] roles = await Task.WhenAll(slice.Select(user =>
            identityAdmin.GetClientRolesAsync(user.Id, clientId, cancellationToken)));
        if (roles.FirstOrDefault(result => result.IsFailure) is { IsFailure: true } failed)
        {
            return failed.Error;
        }

        AppUserResponse[] items =
        [
            .. slice.Select((user, index) =>
                new AppUserResponse(user.Id, user.Username, user.Phone, user.Email, StaffRoles.Of(roles[index].Value, appRoles.Value))),
        ];
        return new AppUsersPageResponse(items, page, pageSize, users.Value.Count > page * pageSize);
    }

    // Держатели ролей приложения (kind=role), без повторов, по логину.
    private async Task<Result<IReadOnlyList<IdentityUser>, Error>> WithRolesAsync(
        string clientId,
        IEnumerable<AppRole> catalogRoles,
        CancellationToken cancellationToken)
    {
        Dictionary<Guid, IdentityUser> users = [];
        foreach (AppRole role in catalogRoles.Where(AppCatalog.IsRole))
        {
            Result<IReadOnlyList<IdentityUser>, Error> holders =
                await identityAdmin.GetRoleUsersAsync(clientId, role.Name, 0, ROLE_USERS_LIMIT, cancellationToken);
            if (holders.IsFailure)
            {
                return holders.Error;
            }

            foreach (IdentityUser user in holders.Value)
            {
                users.TryAdd(user.Id, user);
            }
        }

        return Result.Success<IReadOnlyList<IdentityUser>, Error>([.. users.Values.OrderBy(user => user.Username, StringComparer.Ordinal)]);
    }

    // Точный телефон («+7 (999) 000-00-08» → 79990000008) или почта.
    private async Task<Result<IReadOnlyList<IdentityUser>, Error>> ExactAsync(string search, CancellationToken cancellationToken)
    {
        Result<IdentityUser?, Error> user = await identityAdmin.FindUserExactAsync(GetUsersHandler.SearchText(search)!, cancellationToken);
        return user.IsFailure
            ? user.Error
            : Result.Success<IReadOnlyList<IdentityUser>, Error>(user.Value is null ? [] : [user.Value]);
    }
}

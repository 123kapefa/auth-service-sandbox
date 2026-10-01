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
/// Поиск пользователей с их ролями в приложении — для админа приложения: найти человека и выдать ему роль. Только
/// логин, телефон, почта и роли: блокировка, сессии и прочее об аккаунте — дело админа проекта.
/// </summary>
public sealed class AppUsersHandler(IIdentityAdmin identityAdmin, AppCatalog catalog, IValidator<UsersQuery> validator)
{
    private const int DEFAULT_PAGE_SIZE = 20;

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
        Result<IReadOnlyList<IdentityUser>, Error> found = await identityAdmin.SearchUsersAsync(
            GetUsersHandler.SearchText(query.Search), (page - 1) * pageSize, pageSize + 1, cancellationToken);
        if (found.IsFailure)
        {
            return found.Error;
        }

        IdentityUser[] users = [.. found.Value.Take(pageSize)];
        Result<IReadOnlyList<string>, Error>[] roles = await Task.WhenAll(users.Select(user =>
            identityAdmin.GetClientRolesAsync(user.Id, clientId, cancellationToken)));
        if (roles.FirstOrDefault(result => result.IsFailure) is { IsFailure: true } failed)
        {
            return failed.Error;
        }

        AppUserResponse[] items =
        [
            .. users.Select((user, index) =>
                new AppUserResponse(user.Id, user.Username, user.Phone, user.Email, StaffRoles.Of(roles[index].Value, appRoles.Value))),
        ];
        return new AppUsersPageResponse(items, page, pageSize, found.Value.Count > pageSize);
    }
}

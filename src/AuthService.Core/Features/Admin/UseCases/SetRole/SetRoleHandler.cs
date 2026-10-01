using AuthService.Contracts.Admin;
using AuthService.Core.Abstractions;
using AuthService.Core.Features.Admin.UseCases.AppUserRoles;
using AuthService.Core.Features.Admin.UseCases.GetUser;
using CSharpFunctionalExtensions;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.Extensions.Options;
using PlatformAuth;
using SharedKernel;
using Validation;

namespace AuthService.Core.Features.Admin.UseCases.SetRole;

/// <summary>
/// Смена роли как в боевом сервисе: у пользователя остаётся одна роль приложения сервиса (или только роль по
/// умолчанию — customer). Правила те же, что у PUT …/apps/{clientId}/users/{userId}/roles: недавний вход для
/// ролей с управлением ролями, свою роль менять нельзя, сессии завершаются.
/// </summary>
public sealed class SetRoleHandler(
    IValidator<SetRoleRequest> validator,
    IOptions<JwtAuthenticationOptions> authentication,
    AppCatalog catalog,
    UserRolesHandler userRoles,
    GetUserHandler getUser)
{
    public async Task<Result<UserDetailResponse, Error>> Handle(
        RoleChangeActor actor,
        Guid userId,
        SetRoleRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        ValidationResult validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return validation.ToError();
        }

        string clientId = authentication.Value.Audience;
        Result<IReadOnlyList<AppRole>, Error> roles = await catalog.LoadAsync(clientId, cancellationToken);
        if (roles.IsFailure)
        {
            return roles.Error;
        }

        if (!roles.Value.Where(AppCatalog.IsRole).Any(role => string.Equals(role.Name, request.Role!.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            return GeneralErrors.ValueIsInvalid("role");
        }

        UnitResult<Error> changed = await userRoles.ChangeAsync(actor, clientId, userId, [request.Role!], cancellationToken);
        return changed.IsFailure ? changed.Error : await getUser.Handle(userId, onlyCustomers: false, cancellationToken);
    }
}

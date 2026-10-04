using AuthService.Contracts.Demo;
using AuthService.Core.Authorization;
using CSharpFunctionalExtensions;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using PlatformAuth;
using SharedKernel;

namespace AuthService.Core.Features.Demo;

/// <summary>
/// Демо-эндпоинты этапа 2: по одному на каждую политику и каждое право.
/// Показывают, кто куда проходит; настоящие операции админки появятся на этапе 4.
/// </summary>
public sealed class DemoAccessEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder demo = app.MapGroup("/demo");

        // Политики «роль + активный режим» — контракт боевого сервиса.
        demo.MapGet("/admin-only/", () => Granted(AuthPolicies.ADMIN_ONLY))
            .RequireAuthorization(AuthPolicies.ADMIN_ONLY);
        demo.MapGet("/manager-or-admin/", () => Granted(AuthPolicies.MANAGER_OR_ADMIN))
            .RequireAuthorization(AuthPolicies.MANAGER_OR_ADMIN);

        // Права из карты сервиса по активному режиму.
        MapPermission(demo, "/profile/", AuthServicePermissions.Profiles.VIEW);
        MapPermission(demo, "/users/", AuthServicePermissions.Users.VIEW);
        MapPermission(demo, "/users/block/", AuthServicePermissions.Users.BLOCK);
        MapPermission(demo, "/users/roles/", AuthServicePermissions.Users.MANAGE_ROLES);
        MapPermission(demo, "/audit/", AuthServicePermissions.Audit.VIEW);
    }

    private static void MapPermission(RouteGroupBuilder group, string pattern, string permission) =>
        group.MapGet(pattern, () => Granted(permission)).RequirePermissions(permission);

    private static EndpointResult<DemoAccessResponse> Granted(string access) =>
        Result.Success<DemoAccessResponse, Error>(new DemoAccessResponse(access));
}

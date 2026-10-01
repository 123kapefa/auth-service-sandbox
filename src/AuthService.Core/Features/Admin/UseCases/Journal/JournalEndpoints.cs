using AuthService.Contracts.Admin;
using AuthService.Core.Authorization;
using CSharpFunctionalExtensions;
using Framework.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using PlatformAuth;
using SharedKernel;

namespace AuthService.Core.Features.Admin.UseCases.Journal;

/// <summary>
/// GET /auth/admin/audit?objectId&amp;actorId&amp;action&amp;from&amp;to&amp;page&amp;pageSize — действия сотрудников;
/// GET /auth/admin/login-events?userId&amp;page&amp;pageSize — входы, ошибки входа и выходы (события Keycloak).
/// Право audit.view (админ проекта).
/// </summary>
public sealed class JournalEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapGet("/auth/admin/audit", GetAuditAsync).RequirePermissions(AuthServicePermissions.Audit.VIEW);
        app.MapGet("/auth/admin/login-events", GetLoginEventsAsync).RequirePermissions(AuthServicePermissions.Audit.VIEW);
    }

    private static async Task<EndpointResult<AuditPageResponse>> GetAuditAsync(
        [AsParameters] AuditRequest request,
        [FromServices] JournalHandler handler,
        CancellationToken cancellationToken)
    {
        Result<AuditPageResponse, Error> result = await handler.GetAuditAsync(request, cancellationToken);
        return result;
    }

    private static async Task<EndpointResult<LoginEventsResponse>> GetLoginEventsAsync(
        [AsParameters] LoginEventsRequest request,
        [FromServices] JournalHandler handler,
        CancellationToken cancellationToken)
    {
        Result<LoginEventsResponse, Error> result = await handler.GetLoginEventsAsync(request, cancellationToken);
        return result;
    }
}

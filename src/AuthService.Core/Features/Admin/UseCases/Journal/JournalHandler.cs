using System.Text.Json;
using AuthService.Contracts.Admin;
using AuthService.Core.Abstractions;
using AuthService.Domain.AuditLog;
using CSharpFunctionalExtensions;
using FluentValidation;
using FluentValidation.Results;
using SharedKernel;
using Validation;

namespace AuthService.Core.Features.Admin.UseCases.Journal;

/// <summary>Журнал: действия сотрудников (своя таблица) и входы (события Keycloak).</summary>
public sealed class JournalHandler(
    IAuditLog auditLog,
    IIdentityAdmin identityAdmin,
    IValidator<AuditRequest> auditValidator,
    IValidator<LoginEventsRequest> eventsValidator)
{
    public async Task<Result<AuditPageResponse, Error>> GetAuditAsync(AuditRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        ValidationResult validation = await auditValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return validation.ToError();
        }

        int page = request.Page ?? 1;
        int pageSize = request.PageSize ?? JournalPaging.DEFAULT_PAGE_SIZE;
        AuditPage found = await auditLog.SearchAsync(
            new AuditQuery(request.ObjectId, request.ActorId, request.Action, request.From, request.To, page, pageSize, request.App),
            cancellationToken);

        return new AuditPageResponse([.. found.Items.Select(ToResponse)], page, pageSize, found.HasMore);
    }

    public async Task<Result<LoginEventsResponse, Error>> GetLoginEventsAsync(LoginEventsRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        ValidationResult validation = await eventsValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return validation.ToError();
        }

        int page = request.Page ?? 1;
        int pageSize = request.PageSize ?? JournalPaging.DEFAULT_PAGE_SIZE;
        Result<IReadOnlyList<LoginEvent>, Error> events = await identityAdmin.GetLoginEventsAsync(
            request.UserId, LoginEventTypes.All, (page - 1) * pageSize, pageSize + 1, cancellationToken);
        if (events.IsFailure)
        {
            return events.Error;
        }

        LoginEventResponse[] items =
        [
            .. events.Value.Take(pageSize).Select(found => new LoginEventResponse(
                found.Time, found.Type, found.UserId, found.Username, found.IpAddress, found.ClientId, found.Error)),
        ];
        return new LoginEventsResponse(items, page, pageSize, events.Value.Count > pageSize);
    }

    private static AuditEntryResponse ToResponse(AuditEntry entry)
    {
        using JsonDocument changes = JsonDocument.Parse(entry.Changes);
        return new AuditEntryResponse(
            entry.Id,
            entry.OccurredAt,
            entry.Action,
            entry.ObjectType,
            entry.ObjectId,
            entry.ObjectName,
            changes.RootElement.Clone(),
            entry.ActorId,
            entry.ActorName,
            entry.ActorRole,
            entry.CorrelationId,
            entry.App);
    }
}

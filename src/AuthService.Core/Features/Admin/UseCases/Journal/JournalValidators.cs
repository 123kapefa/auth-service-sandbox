using AuthService.Contracts.Admin;
using FluentValidation;
using SharedKernel;
using Validation;

namespace AuthService.Core.Features.Admin.UseCases.Journal;

/// <summary>Страница — с 1, размер — от 1 до 100; период — from не позже to.</summary>
public sealed class AuditRequestValidator : AbstractValidator<AuditRequest>
{
    public AuditRequestValidator()
    {
        RuleFor(request => request.Page).GreaterThanOrEqualTo(1).When(request => request.Page is not null)
            .WithError(GeneralErrors.OutOfRange("page", 1));
        RuleFor(request => request.PageSize).InclusiveBetween(1, JournalPaging.MAX_PAGE_SIZE).When(request => request.PageSize is not null)
            .WithError(GeneralErrors.OutOfRange("pageSize", 1, JournalPaging.MAX_PAGE_SIZE));
        RuleFor(request => request.From).LessThanOrEqualTo(request => request.To)
            .When(request => request.From is not null && request.To is not null)
            .WithError(GeneralErrors.ValueIsInvalid("from"));
    }
}

/// <summary>Страница — с 1, размер — от 1 до 100.</summary>
public sealed class LoginEventsRequestValidator : AbstractValidator<LoginEventsRequest>
{
    public LoginEventsRequestValidator()
    {
        RuleFor(request => request.Page).GreaterThanOrEqualTo(1).When(request => request.Page is not null)
            .WithError(GeneralErrors.OutOfRange("page", 1));
        RuleFor(request => request.PageSize).InclusiveBetween(1, JournalPaging.MAX_PAGE_SIZE).When(request => request.PageSize is not null)
            .WithError(GeneralErrors.OutOfRange("pageSize", 1, JournalPaging.MAX_PAGE_SIZE));
    }
}

internal static class JournalPaging
{
    public const int MAX_PAGE_SIZE = 100;
    public const int DEFAULT_PAGE_SIZE = 20;
}

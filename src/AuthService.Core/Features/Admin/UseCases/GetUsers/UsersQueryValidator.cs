using AuthService.Contracts.Admin;
using FluentValidation;
using SharedKernel;
using Validation;

namespace AuthService.Core.Features.Admin.UseCases.GetUsers;

/// <summary>Страница — с 1; размер — от 1 до 100; поиск — до 100 символов.</summary>
public sealed class UsersQueryValidator : AbstractValidator<UsersQuery>
{
    public const int MAX_PAGE_SIZE = 100;

    public UsersQueryValidator()
    {
        RuleFor(query => query.Page)
            .GreaterThanOrEqualTo(1)
            .When(query => query.Page is not null)
            .WithError(GeneralErrors.OutOfRange("page", 1));
        RuleFor(query => query.PageSize)
            .InclusiveBetween(1, MAX_PAGE_SIZE)
            .When(query => query.PageSize is not null)
            .WithError(GeneralErrors.OutOfRange("pageSize", 1, MAX_PAGE_SIZE));
        RuleFor(query => query.Search)
            .MaximumLength(100)
            .WithError(GeneralErrors.LengthIsInvalid("search", max: 100));
    }
}

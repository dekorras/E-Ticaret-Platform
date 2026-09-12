using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Accounting;
using MediatR;

namespace Dekorras.Application.Accounting.Queries;

public sealed record ExpenseDto(Guid Id, string CategoryName, decimal AmountTry, DateTime ExpenseDateUtc, string? Description);

public sealed record GetExpensesQuery : IRequest<IReadOnlyCollection<ExpenseDto>>;

public sealed class GetExpensesQueryHandler(IUnitOfWork unitOfWork) : IRequestHandler<GetExpensesQuery, IReadOnlyCollection<ExpenseDto>>
{
    public Task<IReadOnlyCollection<ExpenseDto>> Handle(GetExpensesQuery request, CancellationToken cancellationToken)
    {
        var expenses = unitOfWork.Repository<Expense>().Query()
            .OrderByDescending(e => e.ExpenseDateUtc)
            .Select(e => new ExpenseDto(e.Id, e.CategoryName, e.AmountTry, e.ExpenseDateUtc, e.Description))
            .ToList()
            .AsReadOnly();

        return Task.FromResult<IReadOnlyCollection<ExpenseDto>>(expenses);
    }
}

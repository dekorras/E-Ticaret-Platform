using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Accounting;
using FluentValidation;
using MediatR;

namespace Dekorras.Application.Accounting.Commands;

public sealed record CreateExpenseCommand(string CategoryName, decimal AmountTry, DateTime ExpenseDateUtc, string? Description) : IRequest<Guid>;

public sealed class CreateExpenseCommandValidator : AbstractValidator<CreateExpenseCommand>
{
    public CreateExpenseCommandValidator()
    {
        RuleFor(x => x.CategoryName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.AmountTry).GreaterThan(0);
    }
}

public sealed class CreateExpenseCommandHandler(IUnitOfWork unitOfWork) : IRequestHandler<CreateExpenseCommand, Guid>
{
    public async Task<Guid> Handle(CreateExpenseCommand request, CancellationToken cancellationToken)
    {
        var expense = new Expense(request.CategoryName, request.AmountTry, request.ExpenseDateUtc, request.Description);
        await unitOfWork.Repository<Expense>().AddAsync(expense, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return expense.Id;
    }
}

using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Accounting;
using FluentValidation;
using MediatR;

namespace Dekorras.Application.Accounting.Commands;

public sealed record CreateLedgerAccountCommand(string Name, LedgerAccountType Type, string? TaxOffice, string? TaxNumber, string? Phone) : IRequest<Guid>;

public sealed class CreateLedgerAccountCommandValidator : AbstractValidator<CreateLedgerAccountCommand>
{
    public CreateLedgerAccountCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(300);
    }
}

public sealed class CreateLedgerAccountCommandHandler(IUnitOfWork unitOfWork) : IRequestHandler<CreateLedgerAccountCommand, Guid>
{
    public async Task<Guid> Handle(CreateLedgerAccountCommand request, CancellationToken cancellationToken)
    {
        var account = new LedgerAccount(request.Name, request.Type);
        account.SetTaxInfo(request.TaxOffice, request.TaxNumber);
        account.SetPhone(request.Phone);

        await unitOfWork.Repository<LedgerAccount>().AddAsync(account, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return account.Id;
    }
}

using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Accounting;
using FluentValidation;
using MediatR;

namespace Dekorras.Application.Accounting.Commands;

public sealed record CreateBankAccountCommand(string BankName, string Iban) : IRequest<Guid>;

public sealed class CreateBankAccountCommandValidator : AbstractValidator<CreateBankAccountCommand>
{
    public CreateBankAccountCommandValidator()
    {
        RuleFor(x => x.BankName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Iban).NotEmpty().MaximumLength(34);
    }
}

public sealed class CreateBankAccountCommandHandler(IUnitOfWork unitOfWork) : IRequestHandler<CreateBankAccountCommand, Guid>
{
    public async Task<Guid> Handle(CreateBankAccountCommand request, CancellationToken cancellationToken)
    {
        var account = new BankAccount(request.BankName, request.Iban);
        await unitOfWork.Repository<BankAccount>().AddAsync(account, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return account.Id;
    }
}

public sealed record DepositToBankAccountCommand(Guid BankAccountId, decimal AmountTry) : IRequest<Unit>;

public sealed class DepositToBankAccountCommandValidator : AbstractValidator<DepositToBankAccountCommand>
{
    public DepositToBankAccountCommandValidator() => RuleFor(x => x.AmountTry).GreaterThan(0);
}

public sealed class DepositToBankAccountCommandHandler(IUnitOfWork unitOfWork) : IRequestHandler<DepositToBankAccountCommand, Unit>
{
    public async Task<Unit> Handle(DepositToBankAccountCommand request, CancellationToken cancellationToken)
    {
        var repository = unitOfWork.Repository<BankAccount>();
        var account = await repository.GetByIdAsync(request.BankAccountId, cancellationToken)
            ?? throw new KeyNotFoundException($"'{request.BankAccountId}' numaralı banka hesabı bulunamadı.");

        account.Deposit(request.AmountTry);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}

public sealed record WithdrawFromBankAccountCommand(Guid BankAccountId, decimal AmountTry) : IRequest<Unit>;

public sealed class WithdrawFromBankAccountCommandValidator : AbstractValidator<WithdrawFromBankAccountCommand>
{
    public WithdrawFromBankAccountCommandValidator() => RuleFor(x => x.AmountTry).GreaterThan(0);
}

public sealed class WithdrawFromBankAccountCommandHandler(IUnitOfWork unitOfWork) : IRequestHandler<WithdrawFromBankAccountCommand, Unit>
{
    public async Task<Unit> Handle(WithdrawFromBankAccountCommand request, CancellationToken cancellationToken)
    {
        var repository = unitOfWork.Repository<BankAccount>();
        var account = await repository.GetByIdAsync(request.BankAccountId, cancellationToken)
            ?? throw new KeyNotFoundException($"'{request.BankAccountId}' numaralı banka hesabı bulunamadı.");

        account.Withdraw(request.AmountTry); // yetersiz bakiyede DomainException fırlatır
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}

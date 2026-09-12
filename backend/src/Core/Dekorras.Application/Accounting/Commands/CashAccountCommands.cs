using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Accounting;
using FluentValidation;
using MediatR;

namespace Dekorras.Application.Accounting.Commands;

public sealed record CreateCashRegisterCommand(string Name) : IRequest<Guid>;

public sealed class CreateCashRegisterCommandValidator : AbstractValidator<CreateCashRegisterCommand>
{
    public CreateCashRegisterCommandValidator() => RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
}

public sealed class CreateCashRegisterCommandHandler(IUnitOfWork unitOfWork) : IRequestHandler<CreateCashRegisterCommand, Guid>
{
    public async Task<Guid> Handle(CreateCashRegisterCommand request, CancellationToken cancellationToken)
    {
        var register = new CashRegister(request.Name);
        await unitOfWork.Repository<CashRegister>().AddAsync(register, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return register.Id;
    }
}

public sealed record DepositToCashRegisterCommand(Guid CashRegisterId, decimal AmountTry) : IRequest<Unit>;

public sealed class DepositToCashRegisterCommandValidator : AbstractValidator<DepositToCashRegisterCommand>
{
    public DepositToCashRegisterCommandValidator() => RuleFor(x => x.AmountTry).GreaterThan(0);
}

public sealed class DepositToCashRegisterCommandHandler(IUnitOfWork unitOfWork) : IRequestHandler<DepositToCashRegisterCommand, Unit>
{
    public async Task<Unit> Handle(DepositToCashRegisterCommand request, CancellationToken cancellationToken)
    {
        var repository = unitOfWork.Repository<CashRegister>();
        var register = await repository.GetByIdAsync(request.CashRegisterId, cancellationToken)
            ?? throw new KeyNotFoundException($"'{request.CashRegisterId}' numaralı kasa bulunamadı.");

        register.Deposit(request.AmountTry);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}

public sealed record WithdrawFromCashRegisterCommand(Guid CashRegisterId, decimal AmountTry) : IRequest<Unit>;

public sealed class WithdrawFromCashRegisterCommandValidator : AbstractValidator<WithdrawFromCashRegisterCommand>
{
    public WithdrawFromCashRegisterCommandValidator() => RuleFor(x => x.AmountTry).GreaterThan(0);
}

public sealed class WithdrawFromCashRegisterCommandHandler(IUnitOfWork unitOfWork) : IRequestHandler<WithdrawFromCashRegisterCommand, Unit>
{
    public async Task<Unit> Handle(WithdrawFromCashRegisterCommand request, CancellationToken cancellationToken)
    {
        var repository = unitOfWork.Repository<CashRegister>();
        var register = await repository.GetByIdAsync(request.CashRegisterId, cancellationToken)
            ?? throw new KeyNotFoundException($"'{request.CashRegisterId}' numaralı kasa bulunamadı.");

        register.Withdraw(request.AmountTry); // yetersiz bakiyede DomainException fırlatır
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}

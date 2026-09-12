using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Accounting;
using MediatR;

namespace Dekorras.Application.Accounting.Queries;

public sealed record CashRegisterDto(Guid Id, string Name, decimal BalanceTry);
public sealed record BankAccountDto(Guid Id, string BankName, string Iban, decimal BalanceTry);

public sealed record GetCashRegistersQuery : IRequest<IReadOnlyCollection<CashRegisterDto>>;

public sealed class GetCashRegistersQueryHandler(IUnitOfWork unitOfWork) : IRequestHandler<GetCashRegistersQuery, IReadOnlyCollection<CashRegisterDto>>
{
    public Task<IReadOnlyCollection<CashRegisterDto>> Handle(GetCashRegistersQuery request, CancellationToken cancellationToken)
    {
        var registers = unitOfWork.Repository<CashRegister>().Query()
            .OrderBy(r => r.Name)
            .Select(r => new CashRegisterDto(r.Id, r.Name, r.BalanceTry))
            .ToList()
            .AsReadOnly();

        return Task.FromResult<IReadOnlyCollection<CashRegisterDto>>(registers);
    }
}

public sealed record GetBankAccountsQuery : IRequest<IReadOnlyCollection<BankAccountDto>>;

public sealed class GetBankAccountsQueryHandler(IUnitOfWork unitOfWork) : IRequestHandler<GetBankAccountsQuery, IReadOnlyCollection<BankAccountDto>>
{
    public Task<IReadOnlyCollection<BankAccountDto>> Handle(GetBankAccountsQuery request, CancellationToken cancellationToken)
    {
        var accounts = unitOfWork.Repository<BankAccount>().Query()
            .OrderBy(a => a.BankName)
            .Select(a => new BankAccountDto(a.Id, a.BankName, a.Iban, a.BalanceTry))
            .ToList()
            .AsReadOnly();

        return Task.FromResult<IReadOnlyCollection<BankAccountDto>>(accounts);
    }
}

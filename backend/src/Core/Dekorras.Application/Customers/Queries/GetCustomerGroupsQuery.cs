using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Customers;
using MediatR;

namespace Dekorras.Application.Customers.Queries;

public sealed record CustomerGroupDto(Guid Id, string Name, bool ShowPricesOnStorefront);

public sealed record GetCustomerGroupsQuery : IRequest<IReadOnlyCollection<CustomerGroupDto>>;

public sealed class GetCustomerGroupsQueryHandler(IUnitOfWork unitOfWork) : IRequestHandler<GetCustomerGroupsQuery, IReadOnlyCollection<CustomerGroupDto>>
{
    public Task<IReadOnlyCollection<CustomerGroupDto>> Handle(GetCustomerGroupsQuery request, CancellationToken cancellationToken)
    {
        var groups = unitOfWork.Repository<CustomerGroup>().Query()
            .OrderBy(g => g.Name)
            .Select(g => new CustomerGroupDto(g.Id, g.Name, g.ShowPricesOnStorefront))
            .ToList()
            .AsReadOnly();

        return Task.FromResult<IReadOnlyCollection<CustomerGroupDto>>(groups);
    }
}

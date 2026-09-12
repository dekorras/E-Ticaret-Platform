using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.SystemAdmin;
using MediatR;

namespace Dekorras.Application.SystemAdmin.Queries;

public sealed record GetMyLastSelectedModuleQuery(string IdentityUserId) : IRequest<AppModule?>;

public sealed class GetMyLastSelectedModuleQueryHandler(IUnitOfWork unitOfWork) : IRequestHandler<GetMyLastSelectedModuleQuery, AppModule?>
{
    public Task<AppModule?> Handle(GetMyLastSelectedModuleQuery request, CancellationToken cancellationToken)
    {
        var lastModule = unitOfWork.Repository<AdminProfile>().Query()
            .Where(p => p.IdentityUserId == request.IdentityUserId)
            .Select(p => p.LastSelectedModule)
            .FirstOrDefault();

        return Task.FromResult(lastModule);
    }
}

using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Catalog;
using MediatR;

namespace Dekorras.Application.Catalog.Queries;

public sealed record ProductVideoDto(Guid Id, string Url, string? Title);

public sealed record GetProductVideosQuery(Guid ProductId) : IRequest<IReadOnlyCollection<ProductVideoDto>>;

public sealed class GetProductVideosQueryHandler(IUnitOfWork unitOfWork) : IRequestHandler<GetProductVideosQuery, IReadOnlyCollection<ProductVideoDto>>
{
    public Task<IReadOnlyCollection<ProductVideoDto>> Handle(GetProductVideosQuery request, CancellationToken cancellationToken)
    {
        var videos = unitOfWork.Repository<ProductVideo>().Query()
            .Where(v => v.ProductId == request.ProductId)
            .Select(v => new ProductVideoDto(v.Id, v.Url, v.Title))
            .ToList()
            .AsReadOnly();

        return Task.FromResult<IReadOnlyCollection<ProductVideoDto>>(videos);
    }
}

using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Catalog;
using MediatR;

namespace Dekorras.Application.Catalog.Queries;

public sealed record ProductAttributeDto(Guid Id, string Name);

/// <summary>TÜM ürünler arasında paylaşılan global özellik türü listesi (ör. Renk, Malzeme) - Admin'de
/// yeni bir değer eklerken hangi türlerin seçilebileceğini göstermek için kullanılır.</summary>
public sealed record GetProductAttributesQuery : IRequest<IReadOnlyCollection<ProductAttributeDto>>;

public sealed class GetProductAttributesQueryHandler(IUnitOfWork unitOfWork) : IRequestHandler<GetProductAttributesQuery, IReadOnlyCollection<ProductAttributeDto>>
{
    public Task<IReadOnlyCollection<ProductAttributeDto>> Handle(GetProductAttributesQuery request, CancellationToken cancellationToken)
    {
        var attributes = unitOfWork.Repository<ProductAttribute>().Query()
            .OrderBy(a => a.Name)
            .Select(a => new ProductAttributeDto(a.Id, a.Name))
            .ToList()
            .AsReadOnly();

        return Task.FromResult<IReadOnlyCollection<ProductAttributeDto>>(attributes);
    }
}

public sealed record ProductAttributeValueDto(Guid Id, Guid ProductAttributeId, string AttributeName, string Value);

/// <summary>Bir ürünün kendi özellik değerleri (Admin düzenleme ekranı için).</summary>
public sealed record GetProductAttributeValuesQuery(Guid ProductId) : IRequest<IReadOnlyCollection<ProductAttributeValueDto>>;

public sealed class GetProductAttributeValuesQueryHandler(IUnitOfWork unitOfWork) : IRequestHandler<GetProductAttributeValuesQuery, IReadOnlyCollection<ProductAttributeValueDto>>
{
    public Task<IReadOnlyCollection<ProductAttributeValueDto>> Handle(GetProductAttributeValuesQuery request, CancellationToken cancellationToken)
    {
        // OrderBy, DTO'nun (record) yapıcıdan gelen AttributeName özelliği üzerinden EF LINQ'e
        // ÇEVRİLEMEZ - materyalize ettikten SONRA bellekte sıralanır (GetCampaignsQuery'deki
        // decimal.TryParse'ın SQL'e çevrilememesiyle aynı sınıf sorun).
        var values = unitOfWork.Repository<ProductAttributeValue>().Query()
            .Where(v => v.ProductId == request.ProductId)
            .Join(unitOfWork.Repository<ProductAttribute>().Query(),
                v => v.ProductAttributeId,
                a => a.Id,
                (v, a) => new ProductAttributeValueDto(v.Id, v.ProductAttributeId, a.Name, v.Value))
            .ToList()
            .OrderBy(v => v.AttributeName)
            .ToList()
            .AsReadOnly();

        return Task.FromResult<IReadOnlyCollection<ProductAttributeValueDto>>(values);
    }
}

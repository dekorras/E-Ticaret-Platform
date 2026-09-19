using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Catalog;
using FluentValidation;
using MediatR;

namespace Dekorras.Application.Catalog.Commands;

/// <summary>Admin panelindeki ürün düzenleme ekranında "Kategoriler" artık AYRI bir sekme (bkz.
/// backend/README.md) - bu sekmenin kendi "Kaydet" butonu var, tüm ürünü (fiyat/stok/SEO vb.)
/// yeniden göndermek ZORUNDA kalmadan yalnızca kategori atamasını güncelleyebiliyor -
/// `SetProductTrackStockCommand` ile AYNI "tek alanlık, bağımsız kaydet" kalıbı.</summary>
public sealed record SetProductCategoriesCommand(Guid ProductId, IReadOnlyCollection<Guid> CategoryIds) : IRequest<Unit>;

public sealed class SetProductCategoriesCommandValidator : AbstractValidator<SetProductCategoriesCommand>
{
    public SetProductCategoriesCommandValidator()
    {
        RuleFor(x => x.CategoryIds).NotEmpty().WithMessage("Ürün en az bir kategoriye atanmalıdır.");
    }
}

public sealed class SetProductCategoriesCommandHandler(IUnitOfWork unitOfWork) : IRequestHandler<SetProductCategoriesCommand, Unit>
{
    public async Task<Unit> Handle(SetProductCategoriesCommand request, CancellationToken cancellationToken)
    {
        var repository = unitOfWork.Repository<Product>();
        var product = await repository.GetByIdAsync(request.ProductId, cancellationToken)
            ?? throw new KeyNotFoundException($"'{request.ProductId}' numaralı ürün bulunamadı.");

        // UpdateProductCommandHandler'daki AYNI gereklilik - koleksiyon yüklenmeden SetCategories
        // çağrılırsa mevcut atamalar bulunamaz sanılır, kaldırma sessizce çalışmaz.
        await repository.LoadCollectionAsync(product, p => p.ProductCategories, cancellationToken);

        product.SetCategories(request.CategoryIds);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}

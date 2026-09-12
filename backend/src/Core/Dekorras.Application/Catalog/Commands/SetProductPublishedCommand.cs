using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Catalog;
using MediatR;

namespace Dekorras.Application.Catalog.Commands;

public sealed record SetProductPublishedCommand(Guid Id, bool Published) : IRequest<Unit>;

public sealed class SetProductPublishedCommandHandler(IUnitOfWork unitOfWork) : IRequestHandler<SetProductPublishedCommand, Unit>
{
    public async Task<Unit> Handle(SetProductPublishedCommand request, CancellationToken cancellationToken)
    {
        var repository = unitOfWork.Repository<Product>();
        var product = await repository.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"'{request.Id}' numaralı ürün bulunamadı.");

        // Update(product) BİLİNÇLİ OLARAK çağrılmaz - bkz. TransitionOrderStatusCommand'daki not.
        if (request.Published) product.Publish(); else product.Unpublish();

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}

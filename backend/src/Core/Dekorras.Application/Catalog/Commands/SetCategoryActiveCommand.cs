using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Catalog;
using MediatR;

namespace Dekorras.Application.Catalog.Commands;

public sealed record SetCategoryActiveCommand(Guid Id, bool IsActive) : IRequest<Unit>;

public sealed class SetCategoryActiveCommandHandler(IUnitOfWork unitOfWork) : IRequestHandler<SetCategoryActiveCommand, Unit>
{
    public async Task<Unit> Handle(SetCategoryActiveCommand request, CancellationToken cancellationToken)
    {
        var repository = unitOfWork.Repository<Category>();
        var category = await repository.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"'{request.Id}' numaralı kategori bulunamadı.");

        // Update(category) BİLİNÇLİ OLARAK çağrılmaz - bkz. TransitionOrderStatusCommand'daki not.
        if (request.IsActive) category.Activate(); else category.Deactivate();

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}

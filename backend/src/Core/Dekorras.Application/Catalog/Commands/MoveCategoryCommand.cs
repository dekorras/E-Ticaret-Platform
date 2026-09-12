using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Catalog;
using MediatR;

namespace Dekorras.Application.Catalog.Commands;

public enum MoveDirection { Up, Down }

/// <summary>Aynı üst kategori altındaki kardeşler arasında sırayı değiştirir (DisplayOrder takası).
/// Kabul kriterinin (§14) sürükle-bırak sıralamasına eşdeğer, sürükle-bırak UI'sı olmadan
/// aynı sonucu veren basitleştirilmiş bir uygulamadır.</summary>
public sealed record MoveCategoryCommand(Guid Id, MoveDirection Direction) : IRequest<Unit>;

public sealed class MoveCategoryCommandHandler(IUnitOfWork unitOfWork) : IRequestHandler<MoveCategoryCommand, Unit>
{
    public async Task<Unit> Handle(MoveCategoryCommand request, CancellationToken cancellationToken)
    {
        var repository = unitOfWork.Repository<Category>();
        var category = await repository.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"'{request.Id}' numaralı kategori bulunamadı.");

        var siblings = repository.Query()
            .Where(c => c.ParentCategoryId == category.ParentCategoryId)
            .OrderBy(c => c.DisplayOrder)
            .ToList();

        var index = siblings.FindIndex(c => c.Id == category.Id);
        var swapIndex = request.Direction == MoveDirection.Up ? index - 1 : index + 1;

        if (swapIndex < 0 || swapIndex >= siblings.Count)
            return Unit.Value; // zaten en uçta, yapılacak bir şey yok

        var swapTarget = siblings[swapIndex];
        var currentDisplayOrder = category.DisplayOrder;
        category.Reorder(swapTarget.DisplayOrder);
        swapTarget.Reorder(currentDisplayOrder);

        // Update() BİLİNÇLİ OLARAK çağrılmaz - bkz. TransitionOrderStatusCommand'daki not.
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}

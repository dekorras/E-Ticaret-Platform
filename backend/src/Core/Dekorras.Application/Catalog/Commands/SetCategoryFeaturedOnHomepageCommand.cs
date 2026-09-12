using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Catalog;
using MediatR;

namespace Dekorras.Application.Catalog.Commands;

public sealed record SetCategoryFeaturedOnHomepageCommand(Guid Id, bool IsFeaturedOnHomepage) : IRequest<Unit>;

public sealed class SetCategoryFeaturedOnHomepageCommandHandler(IUnitOfWork unitOfWork) : IRequestHandler<SetCategoryFeaturedOnHomepageCommand, Unit>
{
    public async Task<Unit> Handle(SetCategoryFeaturedOnHomepageCommand request, CancellationToken cancellationToken)
    {
        var repository = unitOfWork.Repository<Category>();
        var category = await repository.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"'{request.Id}' numaralı kategori bulunamadı.");

        category.SetFeaturedOnHomepage(request.IsFeaturedOnHomepage);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}

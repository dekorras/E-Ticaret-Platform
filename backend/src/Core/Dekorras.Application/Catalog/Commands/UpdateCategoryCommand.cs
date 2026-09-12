using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Catalog;
using FluentValidation;
using MediatR;

namespace Dekorras.Application.Catalog.Commands;

public sealed record UpdateCategoryCommand(
    Guid Id,
    string Slug,
    Guid? ParentCategoryId,
    string LanguageCode,
    string Name,
    string? Description,
    string? MetaTitle = null,
    string? MetaDescription = null) : IRequest<Unit>;

public sealed class UpdateCategoryCommandValidator : AbstractValidator<UpdateCategoryCommand>
{
    public UpdateCategoryCommandValidator()
    {
        RuleFor(x => x.Slug).NotEmpty().MaximumLength(200);
        RuleFor(x => x.LanguageCode).NotEmpty().Length(2);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x).Must(x => x.ParentCategoryId != x.Id).WithMessage("Bir kategori kendi üst kategorisi olamaz.");
    }
}

public sealed class UpdateCategoryCommandHandler(IUnitOfWork unitOfWork) : IRequestHandler<UpdateCategoryCommand, Unit>
{
    public async Task<Unit> Handle(UpdateCategoryCommand request, CancellationToken cancellationToken)
    {
        var repository = unitOfWork.Repository<Category>();
        var category = await repository.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"'{request.Id}' numaralı kategori bulunamadı.");

        // Translations yüklenmeden SetTranslation çağrılırsa mevcut çeviri bulunamaz sanılır;
        // EF Core yeni nesneyi "Modified" olarak işaretleyip var olmayan bir satırı UPDATE etmeye
        // çalışır ve DbUpdateConcurrencyException fırlatır (bu regresyon testiyle doğrulanmıştır) -
        // bkz. IRepository<T>.LoadCollectionAsync.
        await repository.LoadCollectionAsync(category, c => c.Translations, cancellationToken);

        // Update(category) BİLİNÇLİ OLARAK çağrılmaz: SetTranslation, kategoriye YENİ bir dil ilk
        // kez eklendiğinde (ör. şu ana kadar yalnızca "tr" varken "en" eklenmesi) yeni bir
        // CategoryTranslation ekler. Update() çağrılsaydı bu yeni çocuğu "Modified" sanıp
        // DbUpdateConcurrencyException fırlatırdı - bkz. TransitionOrderStatusCommand'daki not.
        category.UpdateSlug(request.Slug);
        category.MoveTo(request.ParentCategoryId);
        category.SetTranslation(request.LanguageCode, request.Name, request.Description, request.MetaTitle, request.MetaDescription);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}

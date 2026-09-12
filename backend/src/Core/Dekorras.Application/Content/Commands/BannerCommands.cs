using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Content;
using FluentValidation;
using MediatR;

namespace Dekorras.Application.Content.Commands;

public sealed record CreateBannerCommand(Stream Content, string FileName, string ContentType, string? LinkUrl, int DisplayOrder) : IRequest<Guid>;

public sealed class CreateBannerCommandValidator : AbstractValidator<CreateBannerCommand>
{
    public CreateBannerCommandValidator()
    {
        RuleFor(x => x.FileName).NotEmpty();
        RuleFor(x => x.ContentType).Must(t => t.StartsWith("image/")).WithMessage("Yalnızca görsel dosyaları yüklenebilir.");
    }
}

public sealed class CreateBannerCommandHandler(IUnitOfWork unitOfWork, IFileStorage fileStorage) : IRequestHandler<CreateBannerCommand, Guid>
{
    public async Task<Guid> Handle(CreateBannerCommand request, CancellationToken cancellationToken)
    {
        var url = await fileStorage.UploadAsync("banners", request.FileName, request.Content, request.ContentType, cancellationToken);

        var banner = new Banner(url, request.LinkUrl, request.DisplayOrder);
        await unitOfWork.Repository<Banner>().AddAsync(banner, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return banner.Id;
    }
}

public sealed record UpdateBannerCommand(Guid Id, string? LinkUrl, int DisplayOrder) : IRequest<Unit>;

public sealed class UpdateBannerCommandHandler(IUnitOfWork unitOfWork) : IRequestHandler<UpdateBannerCommand, Unit>
{
    public async Task<Unit> Handle(UpdateBannerCommand request, CancellationToken cancellationToken)
    {
        var repository = unitOfWork.Repository<Banner>();
        var banner = await repository.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"'{request.Id}' numaralı banner bulunamadı.");

        banner.UpdateDetails(request.LinkUrl, request.DisplayOrder);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}

public sealed record SetBannerActiveCommand(Guid Id, bool IsActive) : IRequest<Unit>;

public sealed class SetBannerActiveCommandHandler(IUnitOfWork unitOfWork) : IRequestHandler<SetBannerActiveCommand, Unit>
{
    public async Task<Unit> Handle(SetBannerActiveCommand request, CancellationToken cancellationToken)
    {
        var repository = unitOfWork.Repository<Banner>();
        var banner = await repository.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"'{request.Id}' numaralı banner bulunamadı.");

        if (request.IsActive) banner.Activate(); else banner.Deactivate();
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}

public sealed record RemoveBannerCommand(Guid Id) : IRequest<Unit>;

public sealed class RemoveBannerCommandHandler(IUnitOfWork unitOfWork, IFileStorage fileStorage) : IRequestHandler<RemoveBannerCommand, Unit>
{
    public async Task<Unit> Handle(RemoveBannerCommand request, CancellationToken cancellationToken)
    {
        var repository = unitOfWork.Repository<Banner>();
        var banner = await repository.GetByIdAsync(request.Id, cancellationToken);
        if (banner is null) return Unit.Value;

        var url = banner.ImageUrl;
        repository.Remove(banner);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await fileStorage.DeleteAsync(url, cancellationToken);

        return Unit.Value;
    }
}

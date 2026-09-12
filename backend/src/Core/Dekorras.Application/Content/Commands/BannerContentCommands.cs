using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Content;
using FluentValidation;
using MediatR;

namespace Dekorras.Application.Content.Commands;

public sealed record AddBannerContentCommand(
    Guid BannerNodeId,
    BannerContentType ContentType,
    string? Title,
    string? Subtitle,
    string? Body,
    string? AltText,
    string? LinkUrl,
    string? LinkTarget,
    string? ButtonText,
    string? SettingsJson,
    Stream? ImageContent,
    string? ImageFileName,
    string? ImageContentType) : IRequest<Guid>;

public sealed class AddBannerContentCommandValidator : AbstractValidator<AddBannerContentCommand>
{
    public AddBannerContentCommandValidator()
    {
        RuleFor(x => x.ImageContentType).Must(t => t is null || t.StartsWith("image/"))
            .WithMessage("Yalnızca görsel dosyaları yüklenebilir.");
    }
}

public sealed class AddBannerContentCommandHandler(IUnitOfWork unitOfWork, IFileStorage fileStorage)
    : IRequestHandler<AddBannerContentCommand, Guid>
{
    public async Task<Guid> Handle(AddBannerContentCommand request, CancellationToken cancellationToken)
    {
        var nodeRepository = unitOfWork.Repository<BannerNode>();
        var node = nodeRepository.Query().FirstOrDefault(n => n.Id == request.BannerNodeId)
            ?? throw new KeyNotFoundException($"'{request.BannerNodeId}' numaralı düğüm bulunamadı.");
        if (node.NodeType != BannerNodeType.Column)
            throw new InvalidOperationException("İçerik yalnızca bir kolona eklenebilir.");

        string? imageUrl = null;
        if (request.ImageContent is not null && request.ImageFileName is not null && request.ImageContentType is not null)
            imageUrl = await fileStorage.UploadAsync("banner-zones", request.ImageFileName, request.ImageContent, request.ImageContentType, cancellationToken);

        var contentRepository = unitOfWork.Repository<BannerContent>();
        var siblingCount = contentRepository.Query().Count(c => c.BannerNodeId == request.BannerNodeId);

        var content = new BannerContent(request.BannerNodeId, request.ContentType, siblingCount);
        content.UpdateText(request.Title, request.Subtitle, request.Body, request.ButtonText);
        content.UpdateImage(imageUrl, null, request.AltText);
        content.UpdateLink(request.LinkUrl, request.LinkTarget);
        content.SetSettings(request.SettingsJson);

        await contentRepository.AddAsync(content, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return content.Id;
    }
}

public sealed record UpdateBannerContentCommand(
    Guid Id,
    string? Title,
    string? Subtitle,
    string? Body,
    string? AltText,
    string? LinkUrl,
    string? LinkTarget,
    string? ButtonText,
    string? SettingsJson,
    DateTime? StartDateUtc,
    DateTime? EndDateUtc,
    bool IsActive,
    Stream? NewImageContent,
    string? NewImageFileName,
    string? NewImageContentType) : IRequest<Unit>;

public sealed class UpdateBannerContentCommandHandler(IUnitOfWork unitOfWork, IFileStorage fileStorage)
    : IRequestHandler<UpdateBannerContentCommand, Unit>
{
    public async Task<Unit> Handle(UpdateBannerContentCommand request, CancellationToken cancellationToken)
    {
        var repository = unitOfWork.Repository<BannerContent>();
        var content = await repository.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"'{request.Id}' numaralı içerik bulunamadı.");

        var imageUrl = content.ImageUrl;
        if (request.NewImageContent is not null && request.NewImageFileName is not null && request.NewImageContentType is not null)
        {
            if (imageUrl is not null) await fileStorage.DeleteAsync(imageUrl, cancellationToken);
            imageUrl = await fileStorage.UploadAsync("banner-zones", request.NewImageFileName, request.NewImageContent, request.NewImageContentType, cancellationToken);
        }

        content.UpdateText(request.Title, request.Subtitle, request.Body, request.ButtonText);
        content.UpdateImage(imageUrl, null, request.AltText);
        content.UpdateLink(request.LinkUrl, request.LinkTarget);
        content.SetSettings(request.SettingsJson);
        content.Schedule(request.StartDateUtc, request.EndDateUtc);
        if (request.IsActive) content.Activate(); else content.Deactivate();

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}

public sealed record RemoveBannerContentCommand(Guid Id) : IRequest<Unit>;

public sealed class RemoveBannerContentCommandHandler(IUnitOfWork unitOfWork, IFileStorage fileStorage)
    : IRequestHandler<RemoveBannerContentCommand, Unit>
{
    public async Task<Unit> Handle(RemoveBannerContentCommand request, CancellationToken cancellationToken)
    {
        var repository = unitOfWork.Repository<BannerContent>();
        var content = await repository.GetByIdAsync(request.Id, cancellationToken);
        if (content is null) return Unit.Value;

        var imageUrl = content.ImageUrl;
        repository.Remove(content);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        if (imageUrl is not null) await fileStorage.DeleteAsync(imageUrl, cancellationToken);

        return Unit.Value;
    }
}

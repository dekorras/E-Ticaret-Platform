using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Content;
using FluentValidation;
using MediatR;

namespace Dekorras.Application.Content.Commands;

public sealed record CreateBannerZoneCommand(string Key, string Name, string? Description) : IRequest<Guid>;

public sealed class CreateBannerZoneCommandValidator : AbstractValidator<CreateBannerZoneCommand>
{
    public CreateBannerZoneCommandValidator()
    {
        RuleFor(x => x.Key).NotEmpty().MaximumLength(100).Matches("^[a-z0-9-]+$").WithMessage("Anahtar yalnızca küçük harf, rakam ve tire içerebilir.");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
    }
}

public sealed class CreateBannerZoneCommandHandler(IUnitOfWork unitOfWork) : IRequestHandler<CreateBannerZoneCommand, Guid>
{
    public async Task<Guid> Handle(CreateBannerZoneCommand request, CancellationToken cancellationToken)
    {
        var exists = unitOfWork.Repository<BannerZone>().Query().Any(z => z.Key == request.Key);
        if (exists) throw new InvalidOperationException($"'{request.Key}' anahtarlı bir banner bölgesi zaten var.");

        var zone = new BannerZone(request.Key, request.Name, request.Description);
        await unitOfWork.Repository<BannerZone>().AddAsync(zone, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return zone.Id;
    }
}

public sealed record UpdateBannerZoneCommand(Guid Id, string Name, string? Description) : IRequest<Unit>;

public sealed class UpdateBannerZoneCommandValidator : AbstractValidator<UpdateBannerZoneCommand>
{
    public UpdateBannerZoneCommandValidator() => RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
}

public sealed class UpdateBannerZoneCommandHandler(IUnitOfWork unitOfWork) : IRequestHandler<UpdateBannerZoneCommand, Unit>
{
    public async Task<Unit> Handle(UpdateBannerZoneCommand request, CancellationToken cancellationToken)
    {
        var zone = await unitOfWork.Repository<BannerZone>().GetByIdAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"'{request.Id}' numaralı banner bölgesi bulunamadı.");

        zone.UpdateDetails(request.Name, request.Description);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}

public sealed record SetBannerZoneActiveCommand(Guid Id, bool IsActive) : IRequest<Unit>;

public sealed class SetBannerZoneActiveCommandHandler(IUnitOfWork unitOfWork) : IRequestHandler<SetBannerZoneActiveCommand, Unit>
{
    public async Task<Unit> Handle(SetBannerZoneActiveCommand request, CancellationToken cancellationToken)
    {
        var zone = await unitOfWork.Repository<BannerZone>().GetByIdAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"'{request.Id}' numaralı banner bölgesi bulunamadı.");

        if (request.IsActive) zone.Activate(); else zone.Deactivate();
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}

using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.SystemAdmin;
using MediatR;

namespace Dekorras.Application.SystemAdmin.Commands;

/// <summary>Modül Seçim Ekranı'nda bir kart seçildiğinde çağrılır; "varsayılan olarak son
/// seçilen modül hatırlanır" kuralını uygular (bkz. plan §10.1).</summary>
public sealed record SetLastSelectedModuleCommand(string IdentityUserId, AppModule Module) : IRequest<Unit>;

public sealed class SetLastSelectedModuleCommandHandler(IUnitOfWork unitOfWork) : IRequestHandler<SetLastSelectedModuleCommand, Unit>
{
    public async Task<Unit> Handle(SetLastSelectedModuleCommand request, CancellationToken cancellationToken)
    {
        var repository = unitOfWork.Repository<AdminProfile>();
        var profile = repository.Query().FirstOrDefault(p => p.IdentityUserId == request.IdentityUserId);
        if (profile is null) return Unit.Value;

        // Update(profile) BİLİNÇLİ OLARAK çağrılmaz - bkz. TransitionOrderStatusCommand'daki not.
        profile.RememberLastModule(request.Module);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}

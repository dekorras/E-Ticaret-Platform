using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Identity;
using MediatR;

namespace Dekorras.Application.Identity.Commands;

/// <summary>Çıkış (logout) işleminde çağrılır - jeton bulunamazsa veya zaten iptalse sessizce
/// başarılı sayılır (çağıran taraf için idempotent bir "çıkış yap" davranışı).</summary>
public sealed record RevokeRefreshTokenCommand(string TokenHash) : IRequest<Unit>;

public sealed class RevokeRefreshTokenCommandHandler(IUnitOfWork unitOfWork) : IRequestHandler<RevokeRefreshTokenCommand, Unit>
{
    public async Task<Unit> Handle(RevokeRefreshTokenCommand request, CancellationToken cancellationToken)
    {
        var existing = unitOfWork.Repository<RefreshToken>().Query().FirstOrDefault(t => t.TokenHash == request.TokenHash);
        if (existing is not null && existing.IsActive)
        {
            existing.Revoke();
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return Unit.Value;
    }
}

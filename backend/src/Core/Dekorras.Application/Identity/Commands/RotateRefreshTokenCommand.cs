using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Identity;
using MediatR;

namespace Dekorras.Application.Identity.Commands;

/// <summary>Eski jetonu iptal edip yenisini TEK bir işlemde verir (rotasyon) - çalınmış bir
/// jetonun yeniden kullanılmaya çalışılması durumunda (jeton zaten iptal edilmiş/süresi dolmuşsa)
/// null döner ve çağıran taraf isteği reddetmelidir.</summary>
public sealed record RotateRefreshTokenCommand(string OldTokenHash, string NewTokenHash, DateTime NewExpiresAtUtc) : IRequest<string?>;

public sealed class RotateRefreshTokenCommandHandler(IUnitOfWork unitOfWork) : IRequestHandler<RotateRefreshTokenCommand, string?>
{
    public async Task<string?> Handle(RotateRefreshTokenCommand request, CancellationToken cancellationToken)
    {
        var repository = unitOfWork.Repository<RefreshToken>();
        var existing = repository.Query().FirstOrDefault(t => t.TokenHash == request.OldTokenHash);
        if (existing is null || !existing.IsActive) return null;

        existing.Revoke();
        var newToken = new RefreshToken(existing.IdentityUserId, request.NewTokenHash, request.NewExpiresAtUtc);
        await repository.AddAsync(newToken, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return existing.IdentityUserId;
    }
}

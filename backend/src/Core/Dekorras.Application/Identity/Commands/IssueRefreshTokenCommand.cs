using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Identity;
using MediatR;

namespace Dekorras.Application.Identity.Commands;

/// <summary>Api'nin JWT login/register akışında, ham jetonun SHA-256 özeti burada saklanır -
/// ham jeton yalnızca istemciye döner, hiçbir zaman veritabanına yazılmaz.</summary>
public sealed record IssueRefreshTokenCommand(string IdentityUserId, string TokenHash, DateTime ExpiresAtUtc) : IRequest<Unit>;

public sealed class IssueRefreshTokenCommandHandler(IUnitOfWork unitOfWork) : IRequestHandler<IssueRefreshTokenCommand, Unit>
{
    public async Task<Unit> Handle(IssueRefreshTokenCommand request, CancellationToken cancellationToken)
    {
        var refreshToken = new RefreshToken(request.IdentityUserId, request.TokenHash, request.ExpiresAtUtc);
        await unitOfWork.Repository<RefreshToken>().AddAsync(refreshToken, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}

using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Shipping;
using FluentValidation;
using MediatR;

namespace Dekorras.Application.Ordering.Commands;

/// <summary>`PlaceOrderCommand`'ın checkout anında otomatik oluşturduğu gümrük beyanı taslağını
/// Admin'in düzeltmesi için - ör. bir üründe HS kodu hiç girilmemişse ("Belirtilmemiş" yerine
/// gerçek GTİP kodu elle girilir).</summary>
public sealed record UpdateCustomsDeclarationCommand(Guid OrderId, string HsCodeSummary, decimal DeclaredValueTry, string ContentDescription) : IRequest<Unit>;

public sealed class UpdateCustomsDeclarationCommandValidator : AbstractValidator<UpdateCustomsDeclarationCommand>
{
    public UpdateCustomsDeclarationCommandValidator()
    {
        RuleFor(x => x.HsCodeSummary).NotEmpty();
        RuleFor(x => x.ContentDescription).NotEmpty();
        RuleFor(x => x.DeclaredValueTry).GreaterThan(0);
    }
}

public sealed class UpdateCustomsDeclarationCommandHandler(IUnitOfWork unitOfWork) : IRequestHandler<UpdateCustomsDeclarationCommand, Unit>
{
    public async Task<Unit> Handle(UpdateCustomsDeclarationCommand request, CancellationToken cancellationToken)
    {
        var repository = unitOfWork.Repository<CustomsDeclaration>();
        var declaration = repository.Query().FirstOrDefault(d => d.OrderId == request.OrderId)
            ?? throw new KeyNotFoundException($"'{request.OrderId}' numaralı sipariş için bir gümrük beyanı bulunamadı.");

        // Update(declaration) BİLİNÇLİ OLARAK çağrılmaz - bkz. TransitionOrderStatusCommand'daki not.
        declaration.UpdateDeclaration(request.HsCodeSummary, request.DeclaredValueTry, request.ContentDescription);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}

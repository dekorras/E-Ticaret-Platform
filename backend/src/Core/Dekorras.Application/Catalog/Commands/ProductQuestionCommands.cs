using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Catalog;
using Dekorras.Domain.Customers;
using FluentValidation;
using MediatR;

namespace Dekorras.Application.Catalog.Commands;

public sealed record SubmitProductQuestionCommand(Guid ProductId, string IdentityUserId, string Question) : IRequest<Guid>;

public sealed class SubmitProductQuestionCommandValidator : AbstractValidator<SubmitProductQuestionCommand>
{
    public SubmitProductQuestionCommandValidator()
    {
        RuleFor(x => x.Question).NotEmpty().MaximumLength(1000);
    }
}

public sealed class SubmitProductQuestionCommandHandler(IUnitOfWork unitOfWork) : IRequestHandler<SubmitProductQuestionCommand, Guid>
{
    public async Task<Guid> Handle(SubmitProductQuestionCommand request, CancellationToken cancellationToken)
    {
        var customer = unitOfWork.Repository<Customer>().Query().FirstOrDefault(c => c.IdentityUserId == request.IdentityUserId)
            ?? throw new InvalidOperationException("Soru sorabilmek için önce müşteri profilinizin oluşturulmuş olması gerekir.");

        var question = new ProductQuestion(request.ProductId, customer.Id, request.Question);
        await unitOfWork.Repository<ProductQuestion>().AddAsync(question, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return question.Id;
    }
}

public sealed record AnswerProductQuestionCommand(Guid QuestionId, string Answer) : IRequest<Unit>;

public sealed class AnswerProductQuestionCommandValidator : AbstractValidator<AnswerProductQuestionCommand>
{
    public AnswerProductQuestionCommandValidator()
    {
        RuleFor(x => x.Answer).NotEmpty().MaximumLength(2000);
    }
}

public sealed class AnswerProductQuestionCommandHandler(IUnitOfWork unitOfWork) : IRequestHandler<AnswerProductQuestionCommand, Unit>
{
    public async Task<Unit> Handle(AnswerProductQuestionCommand request, CancellationToken cancellationToken)
    {
        var repository = unitOfWork.Repository<ProductQuestion>();
        var question = await repository.GetByIdAsync(request.QuestionId, cancellationToken)
            ?? throw new KeyNotFoundException($"'{request.QuestionId}' numaralı soru bulunamadı.");

        question.SetAnswer(request.Answer);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}

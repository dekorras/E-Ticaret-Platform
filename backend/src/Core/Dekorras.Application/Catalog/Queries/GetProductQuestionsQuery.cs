using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Catalog;
using Dekorras.Domain.Customers;
using MediatR;

namespace Dekorras.Application.Catalog.Queries;

public sealed record ProductQuestionDto(Guid Id, Guid ProductId, string CustomerName, string Question, string? Answer, DateTime CreatedAtUtc);

/// <summary>Storefront (yalnızca yanıtlanmış - Admin bilinçli olarak `OnlyAnswered: false` geçer)
/// ürün detay sayfasında ve Admin kuyruğunda kullanılır.</summary>
public sealed record GetProductQuestionsQuery(Guid ProductId, bool OnlyAnswered) : IRequest<IReadOnlyCollection<ProductQuestionDto>>;

public sealed class GetProductQuestionsQueryHandler(IUnitOfWork unitOfWork) : IRequestHandler<GetProductQuestionsQuery, IReadOnlyCollection<ProductQuestionDto>>
{
    public Task<IReadOnlyCollection<ProductQuestionDto>> Handle(GetProductQuestionsQuery request, CancellationToken cancellationToken)
    {
        var questions = unitOfWork.Repository<ProductQuestion>().Query()
            .Where(q => q.ProductId == request.ProductId && (!request.OnlyAnswered || q.Answer != null))
            .OrderByDescending(q => q.CreatedAtUtc)
            .Join(unitOfWork.Repository<Customer>().Query(),
                q => q.CustomerId,
                c => c.Id,
                (q, c) => new ProductQuestionDto(q.Id, q.ProductId, c.FullName, q.Question, q.Answer, q.CreatedAtUtc))
            .ToList()
            .AsReadOnly();

        return Task.FromResult<IReadOnlyCollection<ProductQuestionDto>>(questions);
    }
}

/// <summary>Admin kuyruğu - TÜM ürünlerdeki yanıt bekleyen sorular.</summary>
public sealed record GetUnansweredProductQuestionsQuery : IRequest<IReadOnlyCollection<PendingProductQuestionDto>>;

public sealed record PendingProductQuestionDto(Guid Id, Guid ProductId, string ProductName, string CustomerName, string Question, DateTime CreatedAtUtc);

public sealed class GetUnansweredProductQuestionsQueryHandler(IUnitOfWork unitOfWork) : IRequestHandler<GetUnansweredProductQuestionsQuery, IReadOnlyCollection<PendingProductQuestionDto>>
{
    public Task<IReadOnlyCollection<PendingProductQuestionDto>> Handle(GetUnansweredProductQuestionsQuery request, CancellationToken cancellationToken)
    {
        var pending = unitOfWork.Repository<ProductQuestion>().Query()
            .Where(q => q.Answer == null)
            .OrderBy(q => q.CreatedAtUtc)
            .Join(unitOfWork.Repository<Customer>().Query(), q => q.CustomerId, c => c.Id, (q, c) => new { q, c })
            .Join(unitOfWork.Repository<Product>().Query(), x => x.q.ProductId, p => p.Id, (x, p) => new PendingProductQuestionDto(
                x.q.Id, p.Id,
                p.Translations.Where(t => t.LanguageCode == "tr").Select(t => t.Name).FirstOrDefault() ?? p.ProductCode,
                x.c.FullName, x.q.Question, x.q.CreatedAtUtc))
            .ToList()
            .AsReadOnly();

        return Task.FromResult<IReadOnlyCollection<PendingProductQuestionDto>>(pending);
    }
}

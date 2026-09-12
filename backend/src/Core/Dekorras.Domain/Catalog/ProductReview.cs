using Dekorras.Domain.Common;

namespace Dekorras.Domain.Catalog;

public class ProductReview : AuditableEntity
{
    public Guid ProductId { get; private set; }
    public Guid CustomerId { get; private set; }
    public int Rating { get; private set; } // 1-5
    public string Comment { get; private set; } = default!;
    public bool IsApproved { get; private set; }

    private ProductReview() { }

    public ProductReview(Guid productId, Guid customerId, int rating, string comment)
    {
        if (rating is < 1 or > 5) throw new DomainException("Puan 1 ile 5 arasında olmalıdır.");
        ProductId = productId;
        CustomerId = customerId;
        Rating = rating;
        Comment = comment;
    }

    public void Approve() => IsApproved = true;
}

public class ProductQuestion : AuditableEntity
{
    public Guid ProductId { get; private set; }
    public Guid CustomerId { get; private set; }
    public string Question { get; private set; } = default!;
    public string? Answer { get; private set; }
    public DateTime? AnsweredAtUtc { get; private set; }

    private ProductQuestion() { }

    public ProductQuestion(Guid productId, Guid customerId, string question)
    {
        ProductId = productId;
        CustomerId = customerId;
        Question = question;
    }

    public void SetAnswer(string answer)
    {
        Answer = answer;
        AnsweredAtUtc = DateTime.UtcNow;
    }
}

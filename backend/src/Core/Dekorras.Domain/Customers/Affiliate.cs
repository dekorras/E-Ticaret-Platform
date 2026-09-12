using Dekorras.Domain.Common;

namespace Dekorras.Domain.Customers;

public class Affiliate : AuditableEntity
{
    public Guid CustomerId { get; private set; }
    public string Code { get; private set; } = default!;
    public decimal CommissionPercentage { get; private set; }
    public bool AutoApproveCommission { get; private set; }
    public bool IsActive { get; private set; } = true;

    private Affiliate() { }

    public Affiliate(Guid customerId, string code, decimal commissionPercentage, bool autoApproveCommission)
    {
        CustomerId = customerId;
        Code = code;
        CommissionPercentage = commissionPercentage;
        AutoApproveCommission = autoApproveCommission;
    }
}

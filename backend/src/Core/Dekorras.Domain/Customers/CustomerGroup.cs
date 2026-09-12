using Dekorras.Domain.Common;

namespace Dekorras.Domain.Customers;

public class CustomerGroup : AuditableEntity
{
    public string Name { get; private set; } = default!; // Bireysel / Kurumsal
    public bool ShowPricesOnStorefront { get; private set; } = true;

    private CustomerGroup() { }

    public CustomerGroup(string name, bool showPricesOnStorefront = true)
    {
        Name = name;
        ShowPricesOnStorefront = showPricesOnStorefront;
    }

    public void SetShowPricesOnStorefront(bool showPricesOnStorefront) => ShowPricesOnStorefront = showPricesOnStorefront;
}

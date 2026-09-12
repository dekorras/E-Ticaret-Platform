using Dekorras.Application.Common;
using Dekorras.Application.Common.Interfaces;
using Dekorras.Domain.Accounting;
using Dekorras.Domain.Customers;
using Dekorras.Domain.Ordering;
using MediatR;

namespace Dekorras.Application.Accounting.EventHandlers;

/// <summary>
/// Kabul kriteri: "Bir sipariş Tamamlandı durumuna geçtiğinde Muhasebe modülünde otomatik olarak
/// ilgili cari hesaba bağlı bir fatura/irsaliye taslağı oluşmalı" (bkz. plan §10.2). Kullanıcı
/// yalnızca bu taslağı onaylayıp e-Fatura/e-Arşiv olarak kesecektir (IEInvoiceProvider ile).
/// </summary>
public sealed class OrderCompletedEventHandler(IUnitOfWork unitOfWork)
    : INotificationHandler<DomainEventNotification<OrderCompletedEvent>>
{
    public async Task Handle(DomainEventNotification<OrderCompletedEvent> notification, CancellationToken cancellationToken)
    {
        var domainEvent = notification.DomainEvent;

        var ledgerAccounts = unitOfWork.Repository<LedgerAccount>();
        var ledgerAccount = ledgerAccounts.Query().FirstOrDefault(a => a.LinkedCustomerId == domainEvent.CustomerId);

        if (ledgerAccount is null)
        {
            var customer = await unitOfWork.Repository<Customer>().GetByIdAsync(domainEvent.CustomerId, cancellationToken);
            ledgerAccount = new LedgerAccount(customer?.FullName ?? "Bilinmeyen Müşteri", LedgerAccountType.Customer, domainEvent.CustomerId);
            await ledgerAccounts.AddAsync(ledgerAccount, cancellationToken);
        }

        var order = await unitOfWork.Repository<Order>().GetByIdAsync(domainEvent.OrderId, cancellationToken);

        var invoiceNumber = $"TASLAK-{domainEvent.OrderId:N}"[..20];
        var invoice = new Invoice(invoiceNumber, ledgerAccount.Id, domainEvent.OrderId);

        if (order is not null)
        {
            foreach (var item in order.Items)
                invoice.AddLine(item.ProductName, item.UnitPriceTry, item.Quantity, item.TaxRatePercentage);
        }

        await unitOfWork.Repository<Invoice>().AddAsync(invoice, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

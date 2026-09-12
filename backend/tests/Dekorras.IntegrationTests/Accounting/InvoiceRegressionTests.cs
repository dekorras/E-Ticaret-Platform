using Dekorras.Application.Accounting.Commands;
using Dekorras.Application.Accounting.Queries;
using Dekorras.Domain.Accounting;
using Dekorras.Domain.Integrations;
using Dekorras.Persistence;
using Dekorras.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Dekorras.IntegrationTests.Accounting;

/// <summary>Gerçek SQL Server'a karşı çalışır - bkz. LedgerAccountRegressionTests'teki aynı desen.</summary>
public sealed class InvoiceRegressionTests : IAsyncLifetime
{
    private static readonly string ConnectionString = $"Server=localhost\\SQLEXPRESS;Database=DekorrasInvoiceTests;User Id=sa;Password={TestSqlPassword.Value};TrustServerCertificate=True;";

    public async Task InitializeAsync()
    {
        await using var dbContext = CreateDbContext();
        await dbContext.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await using var dbContext = CreateDbContext();
        await dbContext.Database.EnsureDeletedAsync();
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(ConnectionString).Options;
        return new ApplicationDbContext(options);
    }

    [Fact]
    public async Task FaturaDetayi_KalemleriVeCariAdiniDogruDoner_VeIrsaliyelestirmeDurumuGuncellenir()
    {
        Guid accountId, invoiceId;
        await using (var dbContext = CreateDbContext())
        {
            var unitOfWork = new UnitOfWork(dbContext);
            var account = new LedgerAccount("Test Fatura Cari", LedgerAccountType.Customer);
            await unitOfWork.Repository<LedgerAccount>().AddAsync(account, CancellationToken.None);
            await unitOfWork.SaveChangesAsync(CancellationToken.None);
            accountId = account.Id;

            var invoice = new Invoice("TASLAK-TEST-001", accountId, null);
            invoice.AddLine("Test Ürün", 100m, 2, 20m);
            await unitOfWork.Repository<Invoice>().AddAsync(invoice, CancellationToken.None);
            await unitOfWork.SaveChangesAsync(CancellationToken.None);
            invoiceId = invoice.Id;
        }

        await using (var queryContext = CreateDbContext())
        {
            var handler = new GetInvoiceDetailQueryHandler(new UnitOfWork(queryContext));
            var detail = await handler.Handle(new GetInvoiceDetailQuery(invoiceId), CancellationToken.None);

            Assert.NotNull(detail);
            Assert.Equal("Test Fatura Cari", detail!.LedgerAccountName);
            Assert.Equal(InvoiceStatus.Draft, detail.Status);
            Assert.Equal(200m, detail.SubTotalTry); // 2 x 100
            Assert.Equal(40m, detail.TaxTotalTry); // %20 KDV
            Assert.Equal(240m, detail.GrandTotalTry);
            Assert.Single(detail.Lines);
        }

        string waybillNumber;
        await using (var dbContext = CreateDbContext())
        {
            var handler = new MarkInvoiceAsWaybilledCommandHandler(new UnitOfWork(dbContext));
            waybillNumber = await handler.Handle(new MarkInvoiceAsWaybilledCommand(invoiceId), CancellationToken.None);
        }

        await using (var verifyContext = CreateDbContext())
        {
            var invoice2 = await verifyContext.Set<Invoice>().FirstAsync(i => i.Id == invoiceId);
            Assert.Equal(InvoiceStatus.Waybilled, invoice2.Status);

            // İrsaliyeleştirme yalnızca Invoice.Status'u değiştirmemeli, GERÇEK bir Waybill
            // kaydı da oluşturmalı (Faz 0/1'den beri domain'de vardı ama hiç kullanılmıyordu).
            var waybill = await verifyContext.Set<Waybill>().FirstOrDefaultAsync(w => w.InvoiceId == invoiceId);
            Assert.NotNull(waybill);
            Assert.Equal(waybillNumber, waybill!.WaybillNumber);
            Assert.Equal(accountId, waybill.LedgerAccountId);

            var detailHandler = new GetInvoiceDetailQueryHandler(new UnitOfWork(verifyContext));
            var detail = await detailHandler.Handle(new GetInvoiceDetailQuery(invoiceId), CancellationToken.None);
            Assert.Equal(waybillNumber, detail!.WaybillNumber);
        }

        // e-Fatura olarak elle kesim kaydı - EInvoiceLog denetim izi bırakmalı.
        Guid providerId;
        await using (var dbContext = CreateDbContext())
        {
            var provider = new IntegrationProvider("test-efatura", ProviderCategory.EInvoice, "Test e-Fatura");
            provider.RecordHealthCheck(success: true, message: null);
            dbContext.Set<IntegrationProvider>().Add(provider);
            await dbContext.SaveChangesAsync();
            providerId = provider.Id;
        }

        await using (var dbContext = CreateDbContext())
        {
            var handler = new IssueInvoiceAsEInvoiceCommandHandler(new UnitOfWork(dbContext));
            await handler.Handle(new IssueInvoiceAsEInvoiceCommand(invoiceId, providerId, "GIB-2026-000123"), CancellationToken.None);
        }

        await using var finalContext = CreateDbContext();
        var finalInvoice = await finalContext.Set<Invoice>().FirstAsync(i => i.Id == invoiceId);
        Assert.Equal(InvoiceStatus.EInvoiceIssued, finalInvoice.Status);
        Assert.Equal("GIB-2026-000123", finalInvoice.InvoiceNumber);

        var log = await finalContext.Set<EInvoiceLog>().FirstAsync(l => l.InvoiceId == invoiceId);
        Assert.True(log.Success);
        Assert.Equal("GIB-2026-000123", log.GibDocumentNumber);
    }
}

using Dekorras.Application.Customers.Commands;
using Dekorras.Application.Customers.Queries;
using Dekorras.Domain.Customers;
using Dekorras.Persistence;
using Dekorras.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Dekorras.IntegrationTests.Customers;

/// <summary>
/// Gerçek SQL Server'a karşı çalışır. Her adım kendi taze DbContext'ini kullanır - bkz.
/// CategoryUpdateRegressionTests'teki açıklama.
/// </summary>
public sealed class CustomerManagementRegressionTests : IAsyncLifetime
{
    private static readonly string ConnectionString = $"Server=localhost\\SQLEXPRESS;Database=DekorrasCustomerTests;User Id=sa;Password={TestSqlPassword.Value};TrustServerCertificate=True;";

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
    public async Task MusteriListesi_MisafirVeUyeyiDogruAyirtEderVeAramaFiltresiCalisir()
    {
        Guid groupId, memberCustomerId, guestCustomerId;
        await using (var dbContext = CreateDbContext())
        {
            var unitOfWork = new UnitOfWork(dbContext);
            var group = new CustomerGroup("Bireysel");
            await unitOfWork.Repository<CustomerGroup>().AddAsync(group, CancellationToken.None);
            await unitOfWork.SaveChangesAsync(CancellationToken.None);
            groupId = group.Id;

            var member = new Customer("identity-abc", "Ayşe Yılmaz", "ayse@test.com", groupId);
            var guest = new Customer("guest-xyz", "Mehmet Demir", "mehmet@test.com", groupId);
            await unitOfWork.Repository<Customer>().AddAsync(member, CancellationToken.None);
            await unitOfWork.Repository<Customer>().AddAsync(guest, CancellationToken.None);
            await unitOfWork.SaveChangesAsync(CancellationToken.None);
            memberCustomerId = member.Id;
            guestCustomerId = guest.Id;
        }

        await using var queryContext = CreateDbContext();
        var handler = new GetCustomersQueryHandler(new UnitOfWork(queryContext));

        var all = await handler.Handle(new GetCustomersQuery(), CancellationToken.None);
        var member2 = Assert.Single(all, c => c.Id == memberCustomerId);
        var guest2 = Assert.Single(all, c => c.Id == guestCustomerId);
        Assert.False(member2.IsGuest);
        Assert.True(guest2.IsGuest);
        Assert.Equal(0, member2.OrderCount);
        Assert.Equal("Bireysel", member2.GroupName);

        var searched = await handler.Handle(new GetCustomersQuery("Ayşe"), CancellationToken.None);
        Assert.Single(searched);
        Assert.Equal(memberCustomerId, searched.Single().Id);
    }

    [Fact]
    public async Task AktifPasifDegistirme_Kalici_OlarakIslenirVeUpdateCagirmadanCalisir()
    {
        Guid customerId;
        await using (var dbContext = CreateDbContext())
        {
            var unitOfWork = new UnitOfWork(dbContext);
            var group = new CustomerGroup("Bireysel");
            await unitOfWork.Repository<CustomerGroup>().AddAsync(group, CancellationToken.None);
            var customer = new Customer("identity-def", "Fatma Kaya", "fatma@test.com", group.Id);
            customer.AddAddress(new Address(customer.Id, "Fatma Kaya", "TR", "Ankara", "Test Cadde No:1", "5001112233"));
            await unitOfWork.Repository<Customer>().AddAsync(customer, CancellationToken.None);
            await unitOfWork.SaveChangesAsync(CancellationToken.None);
            customerId = customer.Id;
        }

        await using (var dbContext = CreateDbContext())
        {
            var handler = new SetCustomerActiveCommandHandler(new UnitOfWork(dbContext));
            await handler.Handle(new SetCustomerActiveCommand(customerId, IsActive: false), CancellationToken.None);
        }

        await using (var verifyContext = CreateDbContext())
        {
            var detailHandler = new GetCustomerDetailQueryHandler(new UnitOfWork(verifyContext));
            var detail = await detailHandler.Handle(new GetCustomerDetailQuery(customerId), CancellationToken.None);
            Assert.NotNull(detail);
            Assert.False(detail!.IsActive);
            var address = Assert.Single(detail.Addresses);
            Assert.Equal("Test Cadde No:1", address.AddressLine1);
        }

        // Aynı müşteriyi tekrar aktifleştirmek - adres koleksiyonu zaten yüklü bir entity üzerinde
        // ikinci kez SaveChanges çağrıldığında yanlışlıkla "Added" durumuna düşüp yinelenen satır
        // oluşturmadığını doğrular.
        await using (var dbContext = CreateDbContext())
        {
            var handler = new SetCustomerActiveCommandHandler(new UnitOfWork(dbContext));
            await handler.Handle(new SetCustomerActiveCommand(customerId, IsActive: true), CancellationToken.None);
        }

        await using var finalContext = CreateDbContext();
        var finalDetailHandler = new GetCustomerDetailQueryHandler(new UnitOfWork(finalContext));
        var finalDetail = await finalDetailHandler.Handle(new GetCustomerDetailQuery(customerId), CancellationToken.None);
        Assert.True(finalDetail!.IsActive);
        Assert.Single(finalDetail.Addresses);
    }

    [Fact]
    public async Task MusteriGrubuDegistirilebilirVeGecersizGrupReddedilir()
    {
        Guid customerId, bireyselGroupId, kurumsalGroupId;
        await using (var dbContext = CreateDbContext())
        {
            var unitOfWork = new UnitOfWork(dbContext);
            var bireysel = new CustomerGroup("Bireysel");
            var kurumsal = new CustomerGroup("Kurumsal");
            await unitOfWork.Repository<CustomerGroup>().AddAsync(bireysel, CancellationToken.None);
            await unitOfWork.Repository<CustomerGroup>().AddAsync(kurumsal, CancellationToken.None);
            await unitOfWork.SaveChangesAsync(CancellationToken.None);
            bireyselGroupId = bireysel.Id;
            kurumsalGroupId = kurumsal.Id;

            var customer = new Customer("identity-grp", "Grup Testi", "grup@test.com", bireyselGroupId);
            await unitOfWork.Repository<Customer>().AddAsync(customer, CancellationToken.None);
            await unitOfWork.SaveChangesAsync(CancellationToken.None);
            customerId = customer.Id;
        }

        await using (var dbContext = CreateDbContext())
        {
            var handler = new SetCustomerGroupCommandHandler(new UnitOfWork(dbContext));
            await handler.Handle(new SetCustomerGroupCommand(customerId, kurumsalGroupId), CancellationToken.None);
        }

        await using (var verifyContext = CreateDbContext())
        {
            var detailHandler = new GetCustomerDetailQueryHandler(new UnitOfWork(verifyContext));
            var detail = await detailHandler.Handle(new GetCustomerDetailQuery(customerId), CancellationToken.None);
            Assert.Equal(kurumsalGroupId, detail!.CustomerGroupId);
            Assert.Equal("Kurumsal", detail.GroupName);
        }

        // Var olmayan bir gruba taşımaya çalışmak reddedilmeli - müşteri sessizce geçersiz bir
        // CustomerGroupId ile kalmamalı.
        await using (var dbContext = CreateDbContext())
        {
            var handler = new SetCustomerGroupCommandHandler(new UnitOfWork(dbContext));
            await Assert.ThrowsAsync<KeyNotFoundException>(() =>
                handler.Handle(new SetCustomerGroupCommand(customerId, Guid.NewGuid()), CancellationToken.None));
        }

        await using var finalContext = CreateDbContext();
        var finalDetailHandler = new GetCustomerDetailQueryHandler(new UnitOfWork(finalContext));
        var finalDetail = await finalDetailHandler.Handle(new GetCustomerDetailQuery(customerId), CancellationToken.None);
        Assert.Equal(kurumsalGroupId, finalDetail!.CustomerGroupId);
    }
}

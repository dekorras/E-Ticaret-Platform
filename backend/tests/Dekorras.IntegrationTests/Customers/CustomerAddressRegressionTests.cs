using Dekorras.Application.Customers.Commands;
using Dekorras.Application.Customers.Queries;
using Dekorras.Domain.Customers;
using Dekorras.Persistence;
using Dekorras.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Dekorras.IntegrationTests.Customers;

/// <summary>Gerçek SQL Server'a karşı çalışır - `Address.SetAsDefaultShipping`/`SetAsDefaultBilling`
/// Faz 0/1'den beri vardı ama hiçbir yerden çağrılamıyordu (bkz. devamı 58). Storefront'ta yeni
/// "Adreslerim" (adres defteri) ekranı bunu kapatıyor - checkout'a entegrasyon BİLİNÇLİ OLARAK bu
/// turun kapsamı dışında.</summary>
public sealed class CustomerAddressRegressionTests : IAsyncLifetime
{
    private static readonly string ConnectionString = $"Server=localhost\\SQLEXPRESS;Database=DekorrasAddressBookTests;User Id=sa;Password={TestSqlPassword.Value};TrustServerCertificate=True;";

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
    public async Task AdresEklenirVarsayilanYapilirVeSilinebilir()
    {
        const string identityUserId = "identity-address-1";
        Guid addressId1, addressId2;

        await using (var dbContext = CreateDbContext())
        {
            var unitOfWork = new UnitOfWork(dbContext);
            var group = new CustomerGroup("Bireysel");
            await unitOfWork.Repository<CustomerGroup>().AddAsync(group, CancellationToken.None);
            await unitOfWork.SaveChangesAsync(CancellationToken.None);

            var customer = new Customer(identityUserId, "Adres Testi", "adres@test.com", group.Id);
            await unitOfWork.Repository<Customer>().AddAsync(customer, CancellationToken.None);
            await unitOfWork.SaveChangesAsync(CancellationToken.None);
        }

        await using (var dbContext = CreateDbContext())
        {
            var handler = new AddCustomerAddressCommandHandler(new UnitOfWork(dbContext));
            addressId1 = await handler.Handle(new AddCustomerAddressCommand(identityUserId, "Alıcı 1", "TR", "İstanbul", "Cadde 1", "5551112233"), CancellationToken.None);
            addressId2 = await handler.Handle(new AddCustomerAddressCommand(identityUserId, "Alıcı 2", "TR", "Ankara", "Cadde 2", "5553334455"), CancellationToken.None);
        }

        await using (var dbContext = CreateDbContext())
        {
            var profile = await new GetMyCustomerProfileQueryHandler(new UnitOfWork(dbContext)).Handle(new GetMyCustomerProfileQuery(identityUserId), CancellationToken.None);
            Assert.Equal(2, profile!.Addresses.Count);
            Assert.All(profile.Addresses, a => Assert.False(a.IsDefaultShipping));
        }

        // Adres 1 teslimat varsayılanı yapılır.
        await using (var dbContext = CreateDbContext())
        {
            await new SetDefaultShippingAddressCommandHandler(new UnitOfWork(dbContext)).Handle(new SetDefaultShippingAddressCommand(addressId1, identityUserId), CancellationToken.None);
        }

        // Sonra adres 2 teslimat varsayılanı yapılır - adres 1'in bayrağı OTOMATİK kalkmalı (aynı
        // anda yalnızca BİR varsayılan olabilir).
        await using (var dbContext = CreateDbContext())
        {
            await new SetDefaultShippingAddressCommandHandler(new UnitOfWork(dbContext)).Handle(new SetDefaultShippingAddressCommand(addressId2, identityUserId), CancellationToken.None);
        }

        await using (var dbContext = CreateDbContext())
        {
            var profile = await new GetMyCustomerProfileQueryHandler(new UnitOfWork(dbContext)).Handle(new GetMyCustomerProfileQuery(identityUserId), CancellationToken.None);
            Assert.False(profile!.Addresses.Single(a => a.Id == addressId1).IsDefaultShipping);
            Assert.True(profile.Addresses.Single(a => a.Id == addressId2).IsDefaultShipping);
        }

        // Adres 1 silinir.
        await using (var dbContext = CreateDbContext())
        {
            await new RemoveCustomerAddressCommandHandler(new UnitOfWork(dbContext)).Handle(new RemoveCustomerAddressCommand(addressId1, identityUserId), CancellationToken.None);
        }

        await using var finalContext = CreateDbContext();
        var finalProfile = await new GetMyCustomerProfileQueryHandler(new UnitOfWork(finalContext)).Handle(new GetMyCustomerProfileQuery(identityUserId), CancellationToken.None);
        Assert.Single(finalProfile!.Addresses);
        Assert.Equal(addressId2, finalProfile.Addresses.Single().Id);
    }

    [Fact]
    public async Task BaskaMusterininAdresineErisimReddedilir()
    {
        const string identityUserIdA = "identity-address-a";
        const string identityUserIdB = "identity-address-b";
        Guid addressOfA;

        await using (var dbContext = CreateDbContext())
        {
            var unitOfWork = new UnitOfWork(dbContext);
            var group = new CustomerGroup("Bireysel");
            await unitOfWork.Repository<CustomerGroup>().AddAsync(group, CancellationToken.None);
            await unitOfWork.SaveChangesAsync(CancellationToken.None);

            var customerA = new Customer(identityUserIdA, "Müşteri A", "musteri-a@test.com", group.Id);
            var customerB = new Customer(identityUserIdB, "Müşteri B", "musteri-b@test.com", group.Id);
            await unitOfWork.Repository<Customer>().AddAsync(customerA, CancellationToken.None);
            await unitOfWork.Repository<Customer>().AddAsync(customerB, CancellationToken.None);
            await unitOfWork.SaveChangesAsync(CancellationToken.None);
        }

        await using (var dbContext = CreateDbContext())
        {
            addressOfA = await new AddCustomerAddressCommandHandler(new UnitOfWork(dbContext))
                .Handle(new AddCustomerAddressCommand(identityUserIdA, "Müşteri A", "TR", "İzmir", "Cadde 3", "5559998877"), CancellationToken.None);
        }

        // Müşteri B, Müşteri A'nın adresini silmeye/varsayılan yapmaya çalışıyor - REDDEDİLMELİ.
        await using (var dbContext = CreateDbContext())
        {
            var handler = new RemoveCustomerAddressCommandHandler(new UnitOfWork(dbContext));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                handler.Handle(new RemoveCustomerAddressCommand(addressOfA, identityUserIdB), CancellationToken.None));
        }

        await using (var dbContext = CreateDbContext())
        {
            var handler = new SetDefaultShippingAddressCommandHandler(new UnitOfWork(dbContext));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                handler.Handle(new SetDefaultShippingAddressCommand(addressOfA, identityUserIdB), CancellationToken.None));
        }

        // Adres HÂLÂ duruyor ve Müşteri A'ya ait.
        await using var finalContext = CreateDbContext();
        var profileA = await new GetMyCustomerProfileQueryHandler(new UnitOfWork(finalContext)).Handle(new GetMyCustomerProfileQuery(identityUserIdA), CancellationToken.None);
        Assert.Single(profileA!.Addresses);
    }
}

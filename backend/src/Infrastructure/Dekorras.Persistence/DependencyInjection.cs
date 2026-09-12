using Dekorras.Application.Common.Interfaces;
using Dekorras.Persistence.Repositories;
using Dekorras.Persistence.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Dekorras.Persistence;

public static class DependencyInjection
{
    public static IServiceCollection AddPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("'DefaultConnection' bağlantı dizesi bulunamadı.");

        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseSqlServer(connectionString, sql => sql.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName)));

        services.AddIdentity<IdentityUser, IdentityRole>(options =>
            {
                options.Password.RequiredLength = 8;
                options.User.RequireUniqueEmail = true;
            })
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddDefaultTokenProviders();

        // AddIdentity varsayılan UserClaimsPrincipalFactory'yi TryAddScoped ile kaydeder;
        // burada AÇIKÇA AddScoped ile üzerine yazıyoruz ki RBAC izin claim'leri oturum
        // çerezine gömülsün (bkz. AppUserClaimsPrincipalFactory).
        services.AddScoped<IUserClaimsPrincipalFactory<IdentityUser>, AppUserClaimsPrincipalFactory>();

        services.AddScoped<IUnitOfWork, UnitOfWork>();

        return services;
    }
}

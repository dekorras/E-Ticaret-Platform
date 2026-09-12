using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Dekorras.Persistence;

/// <summary>Yalnızca `dotnet ef migrations` tasarım zamanı araçları için kullanılır;
/// çalışma zamanında gerçek bağlantı dizesi Dekorras.Api/appsettings.json'dan gelir.</summary>
public class ApplicationDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var sqlPassword = Environment.GetEnvironmentVariable("DEKORRAS_SQL_PASSWORD")
            ?? throw new InvalidOperationException("DEKORRAS_SQL_PASSWORD ortam değişkeni ayarlanmamış - yerel SQLEXPRESS 'sa' şifrenizi bu değişkene atayın.");

        var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
        optionsBuilder.UseSqlServer($"Server=localhost\\SQLEXPRESS;Database=Dekorras;User Id=sa;Password={sqlPassword};TrustServerCertificate=True;");

        return new ApplicationDbContext(optionsBuilder.Options);
    }
}

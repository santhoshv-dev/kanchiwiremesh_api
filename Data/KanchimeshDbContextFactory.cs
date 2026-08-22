using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace KanchimeshAPI.Data;

/// <summary>
/// Supplies the SQL Server model provider for EF migration commands without
/// requiring an application start or a committed connection string.
/// </summary>
public sealed class KanchimeshDbContextFactory : IDesignTimeDbContextFactory<KanchimeshDbContext>
{
    public KanchimeshDbContext CreateDbContext(string[] args)
    {
        var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile($"appsettings.{environment}.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("SqlServer");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            // EF only needs a configured provider to scaffold migrations. A real
            // connection string is still required for database update commands.
            connectionString = "Server=(localdb)\\mssqllocaldb;Database=KanchimeshDesignTime;Trusted_Connection=True;TrustServerCertificate=True";
        }

        var options = new DbContextOptionsBuilder<KanchimeshDbContext>()
            .UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure())
            .Options;
        return new KanchimeshDbContext(options);
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace M365AcademyCrm.Data;

/// <summary>
/// Lets `dotnet ef migrations add` build a CrmDbContext without booting the
/// full web host (and without needing AzureAd config to be valid). Always
/// SQL Server: migrations target production, never the Development-only
/// SQLite database (which is created with EnsureCreated instead).
/// </summary>
public class CrmDbContextFactory : IDesignTimeDbContextFactory<CrmDbContext>
{
    public CrmDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false)
            .AddUserSecrets<Program>(optional: true)
            .Build();

        var connectionString = configuration.GetConnectionString("CrmDb");
        if (string.IsNullOrWhiteSpace(connectionString) || connectionString == "CHANGE_ME")
        {
            // No real connection is needed to generate migrations - the
            // SqlServer provider just needs a syntactically valid string.
            connectionString = "Server=localhost;Database=M365AcademyCrmDesignTime;Trusted_Connection=True;TrustServerCertificate=True;";
        }

        var optionsBuilder = new DbContextOptionsBuilder<CrmDbContext>();
        optionsBuilder.UseSqlServer(connectionString);

        return new CrmDbContext(optionsBuilder.Options);
    }
}

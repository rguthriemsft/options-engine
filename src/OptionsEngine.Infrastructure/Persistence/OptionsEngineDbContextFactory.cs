using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace OptionsEngine.Infrastructure.Persistence;

/// <summary>Design-time factory used by EF Core commands without coupling Infrastructure to the API.</summary>
public sealed class OptionsEngineDbContextFactory : IDesignTimeDbContextFactory<OptionsEngineDbContext>
{
    public OptionsEngineDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("OPTIONSENGINE_CONNECTION_STRING")
            ?? "Data Source=options-engine.db";
        var options = new DbContextOptionsBuilder<OptionsEngineDbContext>()
            .UseSqlite(connectionString)
            .Options;
        return new OptionsEngineDbContext(options);
    }
}

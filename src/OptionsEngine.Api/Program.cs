using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using OptionsEngine.Api.Health;
using OptionsEngine.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("OptionsEngine")
    ?? throw new InvalidOperationException("Connection string 'OptionsEngine' is required.");

builder.Services.AddDbContext<OptionsEngineDbContext>(options => options.UseSqlite(connectionString));
builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database");

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("OptionsEngine.Api.Startup");
    var database = scope.ServiceProvider.GetRequiredService<OptionsEngineDbContext>();
    logger.LogInformation("Applying database migrations during application startup");
    await database.Database.MigrateAsync();
}

app.MapHealthChecks("/health", new HealthCheckOptions { ResponseWriter = HealthResponseWriter.WriteAsync });
app.Run();

public partial class Program;

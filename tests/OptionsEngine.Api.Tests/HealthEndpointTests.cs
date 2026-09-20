using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using OptionsEngine.Application.EntryStrategy;
using OptionsEngine.Application.PositionSizing;

namespace OptionsEngine.Api.Tests;

public sealed class HealthEndpointTests : IAsyncLifetime
{
    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"options-engine-api-{Guid.NewGuid():N}.db");

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        File.Delete(databasePath);
        File.Delete($"{databasePath}-shm");
        File.Delete($"{databasePath}-wal");
        return Task.CompletedTask;
    }

    [Fact]
    public async Task GetHealthReturnsHealthyJsonAfterApplyingMigrations()
    {
        const string connectionStringKey = "ConnectionStrings__OptionsEngine";
        var originalConnectionString = Environment.GetEnvironmentVariable(connectionStringKey);
        Environment.SetEnvironmentVariable(connectionStringKey, $"Data Source={databasePath}");

        try
        {
            using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
            });
            using var client = factory.CreateClient();

            var response = await client.GetAsync("/health");
            var payload = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("Healthy", payload.RootElement.GetProperty("status").GetString());
            Assert.True(File.Exists(databasePath));
        }
        finally
        {
            Environment.SetEnvironmentVariable(connectionStringKey, originalConnectionString);
        }
    }

    [Fact]
    public void PhaseFourOrchestratorAndItsProductionDependenciesResolveFromCompositionRoot()
    {
        const string connectionStringKey = "ConnectionStrings__OptionsEngine";
        var originalConnectionString = Environment.GetEnvironmentVariable(connectionStringKey);
        Environment.SetEnvironmentVariable(connectionStringKey, $"Data Source={databasePath}");
        try
        {
            using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.UseEnvironment("Testing"));
            using var scope = factory.Services.CreateScope();

            Assert.NotNull(scope.ServiceProvider.GetRequiredService<EntryStrategyEvaluationOrchestrator>());
            Assert.NotNull(scope.ServiceProvider.GetRequiredService<IHoldingRepository>());
            Assert.NotNull(scope.ServiceProvider.GetRequiredService<IEntryStrategyMarketDataRepository>());
            Assert.NotNull(scope.ServiceProvider.GetRequiredService<IEarningsDateSource>());
            Assert.NotNull(scope.ServiceProvider.GetRequiredService<IEntryStrategyEvaluationRepository>());
            Assert.NotNull(scope.ServiceProvider.GetRequiredService<EntryStrategyEvaluationPersistenceService>());
            Assert.NotNull(scope.ServiceProvider.GetRequiredService<IEntryStrategyEvaluationWriter>());
            Assert.NotNull(scope.ServiceProvider.GetRequiredService<IPositionSizingHoldingRepository>());
            Assert.NotNull(scope.ServiceProvider.GetRequiredService<IPositionSizingMarketDataRepository>());
            Assert.NotNull(scope.ServiceProvider.GetRequiredService<IOpenShortCallPositionRepository>());
            Assert.NotNull(scope.ServiceProvider.GetRequiredService<IPositionSizingEvaluationOrchestrator>());
            Assert.NotNull(scope.ServiceProvider.GetRequiredService<IPositionSizingEvaluationRepository>());
            Assert.NotNull(scope.ServiceProvider.GetRequiredService<IPositionSizingEvaluationWriter>());
        }
        finally
        {
            Environment.SetEnvironmentVariable(connectionStringKey, originalConnectionString);
        }
    }
}

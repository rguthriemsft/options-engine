using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OptionsEngine.Application.EntryStrategy;

namespace OptionsEngine.Api.Tests;

public sealed class EntryStrategyEndpointSurfaceTests : IAsyncLifetime
{
    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"options-engine-entry-api-{Guid.NewGuid():N}.db");

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        foreach (var path in new[] { databasePath, $"{databasePath}-shm", $"{databasePath}-wal" })
            if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task UnknownEvaluationReturnsStableNotFoundCodeAndMutationRoutesAreNotExposed()
    {
        const string key = "ConnectionStrings__OptionsEngine";
        var previous = Environment.GetEnvironmentVariable(key);
        Environment.SetEnvironmentVariable(key, $"Data Source={databasePath}");
        try
        {
            using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.UseEnvironment("Testing"));
            using var client = factory.CreateClient();

            var missingHolding = await client.PostAsync($"/api/holdings/{Guid.NewGuid()}/entry-evaluations", content: null);
            Assert.Equal(HttpStatusCode.NotFound, missingHolding.StatusCode);
            using var missingHoldingJson = JsonDocument.Parse(await missingHolding.Content.ReadAsStringAsync());
            Assert.Equal("HOLDING_NOT_FOUND", missingHoldingJson.RootElement.GetProperty("code").GetString());

            var get = await client.GetAsync($"/api/entry-evaluations/{Guid.NewGuid()}");
            Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
            using var getJson = JsonDocument.Parse(await get.Content.ReadAsStringAsync());
            Assert.Equal("ENTRY_STRATEGY_EVALUATION_NOT_FOUND", getJson.RootElement.GetProperty("code").GetString());

            foreach (var method in new[] { HttpMethod.Put, HttpMethod.Patch, HttpMethod.Delete })
            {
                var response = await client.SendAsync(new HttpRequestMessage(method,
                    $"/api/entry-evaluations/{Guid.NewGuid()}"));
                Assert.Contains(response.StatusCode, new[] { HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed });
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable(key, previous);
        }
    }

    [Fact]
    public async Task DisabledHoldingMapsToConflictWithStableCode()
    {
        const string key = "ConnectionStrings__OptionsEngine";
        var previous = Environment.GetEnvironmentVariable(key);
        Environment.SetEnvironmentVariable(key, $"Data Source={databasePath}");
        try
        {
            using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<IEntryStrategyEvaluationWriter>();
                    services.AddSingleton<IEntryStrategyEvaluationWriter, DisabledHoldingWriter>();
                });
            });
            using var client = factory.CreateClient();
            var response = await client.PostAsync($"/api/holdings/{Guid.NewGuid()}/entry-evaluations", content: null);

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal("HOLDING_DISABLED", json.RootElement.GetProperty("code").GetString());
        }
        finally
        {
            Environment.SetEnvironmentVariable(key, previous);
        }
    }

    private sealed class DisabledHoldingWriter : IEntryStrategyEvaluationWriter
    {
        public Task<PersistedEntryStrategyEvaluation> CreateAsync(Guid holdingId, DateTimeOffset evaluationTimestampUtc,
            CancellationToken cancellationToken = default) =>
            throw new HoldingDisabledException(holdingId);
    }
}

using FamilyBudget.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace FamilyBudget.Tests;

// Starts one real PostgreSQL container for the whole test class and creates the schema
// by applying the app's EF Core migrations (not EnsureCreated), so the migrations
// themselves are exercised too.
public sealed class PostgresFixture : IAsyncLifetime
{
    // Same major version as local development and production.
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:18").Build();

    private ServiceProvider? _services;

    public IDbContextFactory<FamilyBudgetDbContext> DbFactory { get; private set; } = null!;

    // For tests that need their own database in the same container (e.g. migration tests).
    public string ConnectionString => _container.GetConnectionString();

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();

        // The same registration as in Program.cs, pointed at the container:
        // this gives the service a real IDbContextFactory, not a hand-written fake.
        var services = new ServiceCollection();
        services.AddDbContextFactory<FamilyBudgetDbContext>(options =>
            options.UseNpgsql(_container.GetConnectionString()));

        _services = services.BuildServiceProvider();
        DbFactory = _services.GetRequiredService<IDbContextFactory<FamilyBudgetDbContext>>();

        await using var db = await DbFactory.CreateDbContextAsync();
        await db.Database.MigrateAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_services is not null)
        {
            await _services.DisposeAsync();
        }

        await _container.DisposeAsync();
    }
}

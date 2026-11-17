using FamilyBudget.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace FamilyBudget.Tests;

// The data migration SeedDefaultCategories, run against data that existed before it.
// Uses its own fresh database in the shared container, because the fixture's database
// is already migrated to the latest version.
public class SeedDefaultCategoriesMigrationTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    private const string MigrationSuffix = "_SeedDefaultCategories";

    [Fact]
    public async Task Backfill_GroupWithoutCategoriesGetsDefaultTree_GroupWithOwnCategoryUntouched()
    {
        var connectionString = new NpgsqlConnectionStringBuilder(fixture.ConnectionString)
        {
            Database = $"backfill_{Guid.NewGuid():N}"
        }.ConnectionString;

        var services = new ServiceCollection();
        services.AddDbContextFactory<FamilyBudgetDbContext>(options => options.UseNpgsql(connectionString));
        await using var provider = services.BuildServiceProvider();
        var dbFactory = provider.GetRequiredService<IDbContextFactory<FamilyBudgetDbContext>>();
        var data = new TestData(dbFactory);

        // Step 1: migrate the empty database up to the migration just before the backfill.
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            var migrations = db.Database.GetMigrations().ToList();
            var backfillIndex = migrations.FindIndex(m => m.EndsWith(MigrationSuffix, StringComparison.Ordinal));
            Assert.True(backfillIndex > 0, "Migration SeedDefaultCategories not found.");

            await db.GetService<IMigrator>().MigrateAsync(migrations[backfillIndex - 1]);
        }

        // Step 2: data as it existed before the backfill.
        var emptyGroup = await data.CreateGroupAsync("Group without categories");
        var ownGroup = await data.CreateGroupAsync("Group with own category");
        var own = await data.CreateCategoryAsync(ownGroup.Id, "Own category");

        // Step 3: migrate to the latest version, which runs the backfill.
        await using (var db = await dbFactory.CreateDbContextAsync())
        {
            await db.Database.MigrateAsync();
        }

        ExpectedDefaultTree.AssertMatches(await data.GetCategoriesAsync(emptyGroup.Id));
        Assert.Equal(
            [new CategoryRow(own.Id, "Own category", null, ownGroup.Id)],
            await data.GetCategoriesAsync(ownGroup.Id));
    }
}

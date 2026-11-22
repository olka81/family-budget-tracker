using Microsoft.EntityFrameworkCore;

namespace FamilyBudget.Tests;

// The PurchasePrecision migration gives Price and Quantity the types from DB_SCHEMA.
public class PurchasePrecisionMigrationTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task Columns_HaveIntendedPrecision()
    {
        await using var db = await fixture.DbFactory.CreateDbContextAsync();

        var columns = await db.Database
            .SqlQueryRaw<ColumnPrecision>(
                """
                SELECT column_name AS "Name",
                       data_type AS "DataType",
                       numeric_precision AS "Precision",
                       numeric_scale AS "Scale"
                FROM information_schema.columns
                WHERE table_schema = 'public'
                  AND table_name = 'Purchases'
                  AND column_name IN ('Price', 'Quantity')
                ORDER BY column_name
                """)
            .ToListAsync();

        Assert.Equal(
            [
                new ColumnPrecision { Name = "Price", DataType = "numeric", Precision = 10, Scale = 2 },
                new ColumnPrecision { Name = "Quantity", DataType = "numeric", Precision = 10, Scale = 3 }
            ],
            columns);
    }

    public sealed record ColumnPrecision
    {
        public string Name { get; init; } = "";
        public string DataType { get; init; } = "";
        public int? Precision { get; init; }
        public int? Scale { get; init; }
    }
}

using FamilyBudget.Data;
using FamilyBudget.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FamilyBudget.Services;

public class ProductService(IDbContextFactory<FamilyBudgetDbContext> dbFactory)
{
    // Factory, not DbContext itself: each method creates a short-lived context (Architecture Decision #7)
    private readonly IDbContextFactory<FamilyBudgetDbContext> _dbFactory = dbFactory;

    // The unique index (FamilyGroupId, Name) on Products (Architecture Decision #2).
    private const string NameIndex = "IX_Products_FamilyGroupId_Name";

    public async Task<IReadOnlyList<ProductInfo>> GetAllAsync(int userId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var groupId = await GetGroupIdAsync(db, userId);

        return await db.Products
            .Where(p => p.FamilyGroupId == groupId)
            .OrderBy(p => p.Name)
            .Select(p => new ProductInfo(p.Id, p.Name, p.CategoryId))
            .ToListAsync();
    }

    // Returns the product with this (normalized) name, creating it if it does not exist.
    // An existing product's category is never changed here.
    public async Task<FindOrCreateProductResult> FindOrCreateAsync(int userId, string? name, int? categoryId)
    {
        var normalized = NameNormalizer.Normalize(name);
        if (normalized.Length == 0)
        {
            return new FindOrCreateProductResult(ProductStatus.EmptyName, null, false);
        }

        await using var db = await _dbFactory.CreateDbContextAsync();

        var groupId = await GetGroupIdAsync(db, userId);

        // Checked before the lookup: an invalid category is rejected even if the product exists.
        if (categoryId is not null && !await CategoryInGroupAsync(db, categoryId.Value, groupId))
        {
            return new FindOrCreateProductResult(ProductStatus.CategoryNotFound, null, false);
        }

        var existing = await FindByNameAsync(db, groupId, normalized);
        if (existing is not null)
        {
            return new FindOrCreateProductResult(ProductStatus.Ok, existing, false);
        }

        var product = new Product { Name = normalized, CategoryId = categoryId, FamilyGroupId = groupId };
        db.Products.Add(product);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsPostgresError(ex, PostgresErrorCodes.UniqueViolation, NameIndex))
        {
            // A concurrent request created the same product between our lookup and insert.
            // Forget the failed insert and return the row that won.
            db.ChangeTracker.Clear();
            var winner = await FindByNameAsync(db, groupId, normalized)
                ?? throw new InvalidOperationException(
                    "Unique violation on product name, but the existing product was not found.", ex);
            return new FindOrCreateProductResult(ProductStatus.Ok, winner, false);
        }

        return new FindOrCreateProductResult(
            ProductStatus.Ok, new ProductInfo(product.Id, product.Name, product.CategoryId), true);
    }

    // Renames without merging: a name already used by another product of the group is NameTaken.
    public async Task<ProductStatus> RenameAsync(int userId, int productId, string? name)
    {
        var normalized = NameNormalizer.Normalize(name);
        if (normalized.Length == 0)
        {
            return ProductStatus.EmptyName;
        }

        await using var db = await _dbFactory.CreateDbContextAsync();

        var groupId = await GetGroupIdAsync(db, userId);

        var product = await FindInGroupAsync(db, productId, groupId);
        if (product is null)
        {
            return ProductStatus.NotFound;
        }

        if (product.Name == normalized)
        {
            return ProductStatus.Ok;
        }

        if (await db.Products.AnyAsync(p => p.FamilyGroupId == groupId && p.Name == normalized))
        {
            return ProductStatus.NameTaken;
        }

        product.Name = normalized;
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsPostgresError(ex, PostgresErrorCodes.UniqueViolation, NameIndex))
        {
            // A concurrent request took this name between our check and the update.
            return ProductStatus.NameTaken;
        }

        return ProductStatus.Ok;
    }

    // categoryId = null clears the product's category.
    public async Task<ProductStatus> SetCategoryAsync(int userId, int productId, int? categoryId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var groupId = await GetGroupIdAsync(db, userId);

        var product = await FindInGroupAsync(db, productId, groupId);
        if (product is null)
        {
            return ProductStatus.NotFound;
        }

        if (categoryId is not null && !await CategoryInGroupAsync(db, categoryId.Value, groupId))
        {
            return ProductStatus.CategoryNotFound;
        }

        product.CategoryId = categoryId;
        await db.SaveChangesAsync();

        return ProductStatus.Ok;
    }

    // A product that has purchases cannot be deleted (Purchase -> Product is Restrict).
    public async Task<ProductStatus> DeleteAsync(int userId, int productId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var groupId = await GetGroupIdAsync(db, userId);

        var product = await FindInGroupAsync(db, productId, groupId);
        if (product is null)
        {
            return ProductStatus.NotFound;
        }

        if (await db.Purchases.AnyAsync(p => p.ProductId == productId))
        {
            return ProductStatus.InUse;
        }

        // Tracked Remove (not ExecuteDelete), so a violation arrives as DbUpdateException.
        db.Products.Remove(product);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsPostgresError(ex, PostgresErrorCodes.ForeignKeyViolation))
        {
            // A purchase of this product was added concurrently.
            return ProductStatus.InUse;
        }

        return ProductStatus.Ok;
    }

    private static Task<int> GetGroupIdAsync(FamilyBudgetDbContext db, int userId) =>
        db.Users
            .Where(u => u.Id == userId)
            .Select(u => u.FamilyGroupId)
            .SingleAsync();

    private static Task<bool> CategoryInGroupAsync(FamilyBudgetDbContext db, int categoryId, int groupId) =>
        db.Categories.AnyAsync(c => c.Id == categoryId && c.FamilyGroupId == groupId);

    private static Task<Product?> FindInGroupAsync(FamilyBudgetDbContext db, int productId, int groupId) =>
        db.Products.SingleOrDefaultAsync(p => p.Id == productId && p.FamilyGroupId == groupId);

    private static Task<ProductInfo?> FindByNameAsync(FamilyBudgetDbContext db, int groupId, string normalizedName) =>
        db.Products
            .Where(p => p.FamilyGroupId == groupId && p.Name == normalizedName)
            .Select(p => new ProductInfo(p.Id, p.Name, p.CategoryId))
            .SingleOrDefaultAsync();

    // True when the save failed with this PostgreSQL error code (and, if given, on this constraint).
    private static bool IsPostgresError(DbUpdateException ex, string sqlState, string? constraintName = null) =>
        ex.InnerException is PostgresException pg
        && pg.SqlState == sqlState
        && (constraintName is null || pg.ConstraintName == constraintName);
}

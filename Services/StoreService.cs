using FamilyBudget.Data;
using FamilyBudget.Models;
using Microsoft.EntityFrameworkCore;

namespace FamilyBudget.Services;

public class StoreService(IDbContextFactory<FamilyBudgetDbContext> dbFactory)
{
    // Factory, not DbContext itself: each method creates a short-lived context (Architecture Decision #7)
    private readonly IDbContextFactory<FamilyBudgetDbContext> _dbFactory = dbFactory;

    public async Task<IReadOnlyList<StoreInfo>> GetAllAsync(int userId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var groupId = await GetGroupIdAsync(db, userId);

        return await db.Stores
            .Where(s => s.FamilyGroupId == groupId)
            .OrderBy(s => s.Name)
            .ThenBy(s => s.Id)
            .Select(s => new StoreInfo(s.Id, s.Name))
            .ToListAsync();
    }

    // Duplicate store names are allowed: no uniqueness check.
    public async Task<CreateStoreResult> CreateAsync(int userId, string? name)
    {
        var normalized = NameNormalizer.Normalize(name);
        if (normalized.Length == 0)
        {
            return new CreateStoreResult(StoreStatus.EmptyName, null);
        }

        await using var db = await _dbFactory.CreateDbContextAsync();

        var groupId = await GetGroupIdAsync(db, userId);

        var store = new Store { Name = normalized, FamilyGroupId = groupId };
        db.Stores.Add(store);
        await db.SaveChangesAsync();

        return new CreateStoreResult(StoreStatus.Ok, store.Id);
    }

    public async Task<StoreStatus> RenameAsync(int userId, int storeId, string? name)
    {
        var normalized = NameNormalizer.Normalize(name);
        if (normalized.Length == 0)
        {
            return StoreStatus.EmptyName;
        }

        await using var db = await _dbFactory.CreateDbContextAsync();

        var groupId = await GetGroupIdAsync(db, userId);

        var store = await FindInGroupAsync(db, storeId, groupId);
        if (store is null)
        {
            return StoreStatus.NotFound;
        }

        store.Name = normalized;
        await db.SaveChangesAsync();

        return StoreStatus.Ok;
    }

    // Purchases of the deleted store keep existing: the database sets their StoreId to NULL.
    public async Task<StoreStatus> DeleteAsync(int userId, int storeId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var groupId = await GetGroupIdAsync(db, userId);

        var store = await FindInGroupAsync(db, storeId, groupId);
        if (store is null)
        {
            return StoreStatus.NotFound;
        }

        db.Stores.Remove(store);
        await db.SaveChangesAsync();

        return StoreStatus.Ok;
    }

    private static Task<int> GetGroupIdAsync(FamilyBudgetDbContext db, int userId) =>
        db.Users
            .Where(u => u.Id == userId)
            .Select(u => u.FamilyGroupId)
            .SingleAsync();

    private static Task<Store?> FindInGroupAsync(FamilyBudgetDbContext db, int storeId, int groupId) =>
        db.Stores.SingleOrDefaultAsync(s => s.Id == storeId && s.FamilyGroupId == groupId);
}

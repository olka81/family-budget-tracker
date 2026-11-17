using FamilyBudget.Data;
using FamilyBudget.Models;
using FamilyBudget.Services;
using Microsoft.EntityFrameworkCore;

namespace FamilyBudget.Tests;

// Helpers that prepare and read back rows directly through the DbContext.
// Every call uses its own short-lived context, so assertions always see what is
// really stored in the database, not something cached by a tracked context.
internal sealed class TestData(IDbContextFactory<FamilyBudgetDbContext> dbFactory)
{
    public async Task<FamilyGroup> CreateGroupAsync(string name, Guid? inviteToken = null)
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        var group = new FamilyGroup { Name = name, InviteToken = inviteToken };
        db.FamilyGroups.Add(group);
        await db.SaveChangesAsync();

        return group;
    }

    // UserManager is not needed: a plain row with a unique email is enough for the service.
    public async Task<ApplicationUser> CreateUserAsync(int groupId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        var email = $"user-{Guid.NewGuid():N}@example.com";
        var user = new ApplicationUser
        {
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            SecurityStamp = Guid.NewGuid().ToString(),
            FamilyGroupId = groupId
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        return user;
    }

    public async Task<FamilyGroup?> FindGroupAsync(int groupId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.FamilyGroups.AsNoTracking().SingleOrDefaultAsync(g => g.Id == groupId);
    }

    public async Task<ApplicationUser?> FindUserAsync(int userId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == userId);
    }

    public async Task<int> CountMembersAsync(int groupId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Users.CountAsync(u => u.FamilyGroupId == groupId);
    }

    public async Task<Category> CreateCategoryAsync(int groupId, string name, int? parentId = null)
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        var category = new Category { Name = name, FamilyGroupId = groupId, ParentCategoryId = parentId };
        db.Categories.Add(category);
        await db.SaveChangesAsync();

        return category;
    }

    public async Task<Product> CreateProductAsync(int groupId, string name, int? categoryId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        var product = new Product { Name = name, FamilyGroupId = groupId, CategoryId = categoryId };
        db.Products.Add(product);
        await db.SaveChangesAsync();

        return product;
    }

    public async Task<Product?> FindProductAsync(int productId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Products.AsNoTracking().SingleOrDefaultAsync(p => p.Id == productId);
    }

    public async Task<List<ProductRow>> GetProductsAsync(int groupId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Products
            .Where(p => p.FamilyGroupId == groupId)
            .OrderBy(p => p.Id)
            .Select(p => new ProductRow(p.Id, p.Name, p.CategoryId, p.FamilyGroupId))
            .ToListAsync();
    }

    public async Task<Store> CreateStoreAsync(int groupId, string name)
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        var store = new Store { Name = name, FamilyGroupId = groupId };
        db.Stores.Add(store);
        await db.SaveChangesAsync();

        return store;
    }

    public async Task<List<StoreRow>> GetStoresAsync(int groupId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Stores
            .Where(s => s.FamilyGroupId == groupId)
            .OrderBy(s => s.Id)
            .Select(s => new StoreRow(s.Id, s.Name, s.FamilyGroupId))
            .ToListAsync();
    }

    // No purchases can be created through the app yet, so the row is inserted directly.
    public async Task<Purchase> CreatePurchaseAsync(int groupId, int userId, int productId, int? storeId = null)
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        var purchase = new Purchase
        {
            FamilyGroupId = groupId,
            UserId = userId,
            ProductId = productId,
            StoreId = storeId,
            Price = 1.50m
        };
        db.Purchases.Add(purchase);
        await db.SaveChangesAsync();

        return purchase;
    }

    public async Task<Purchase?> FindPurchaseAsync(int purchaseId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Purchases.AsNoTracking().SingleOrDefaultAsync(p => p.Id == purchaseId);
    }

    // Seeds the default tree for a new group through the production seeding code.
    public async Task<FamilyGroup> CreateGroupWithDefaultCategoriesAsync(string name)
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        var group = new FamilyGroup { Name = name };
        db.FamilyGroups.Add(group);
        DefaultCategories.AddTo(db, group);
        await db.SaveChangesAsync();

        return group;
    }

    public async Task<List<CategoryRow>> GetCategoriesAsync(int groupId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Categories
            .Where(c => c.FamilyGroupId == groupId)
            .OrderBy(c => c.Id)
            .Select(c => new CategoryRow(c.Id, c.Name, c.ParentCategoryId, c.FamilyGroupId))
            .ToListAsync();
    }

    public async Task<int> CountCategoriesAsync(IEnumerable<int> categoryIds)
    {
        var ids = categoryIds.ToList();
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Categories.CountAsync(c => ids.Contains(c.Id));
    }
}

internal sealed record CategoryRow(int Id, string Name, int? ParentId, int GroupId);

internal sealed record ProductRow(int Id, string Name, int? CategoryId, int GroupId);

internal sealed record StoreRow(int Id, string Name, int GroupId);

// The expected default tree, written out independently of the production definition.
// Each entry is the full path from the top level, so comparing the sets of paths checks
// every parent-child pair.
internal static class ExpectedDefaultTree
{
    public static readonly string[] Paths =
    [
        "Продукты питания",
        "Продукты питания/Молочные",
        "Продукты питания/Молочные/Кисломолочка",
        "Продукты питания/Молочные/Масло",
        "Продукты питания/Молочные/Прочее",
        "Продукты питания/Мясные",
        "Продукты питания/Мясные/Для готовки",
        "Продукты питания/Мясные/Готовые к употреблению",
        "Продукты питания/Хлебо-булочные",
        "Продукты питания/Сладкое",
        "Продукты питания/Яйца",
        "Продукты питания/Бакалея",
        "Продукты питания/Овощи и фрукты",
        "Продукты питания/Напитки",
        "Продукты питания/Прочее",
        "Услуги",
        "Аренда",
        "Подписки",
        "Одежда, обувь",
        "Зоотовары",
        "Зоотовары/Корм",
        "Зоотовары/Наполнитель",
        "Зоотовары/Прочее",
        "Зоотовары/Ветеринар",
        "Табак и аксессуары",
        "Общ.транспорт",
    ];

    // Builds "a/b/c" paths from stored rows (parents must be among the rows).
    public static List<string> ToPaths(IReadOnlyList<CategoryRow> rows)
    {
        var byId = rows.ToDictionary(r => r.Id);

        string PathOf(CategoryRow row) =>
            row.ParentId is { } parentId ? $"{PathOf(byId[parentId])}/{row.Name}" : row.Name;

        return rows.Select(PathOf).ToList();
    }

    public static void AssertMatches(IReadOnlyList<CategoryRow> rows)
    {
        Assert.Equal(26, rows.Count);
        Assert.Equal(8, rows.Count(r => r.ParentId is null));
        Assert.Equal(Paths.Order(StringComparer.Ordinal), ToPaths(rows).Order(StringComparer.Ordinal));
    }
}

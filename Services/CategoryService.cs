using FamilyBudget.Data;
using FamilyBudget.Models;
using Microsoft.EntityFrameworkCore;

namespace FamilyBudget.Services;

public class CategoryService(IDbContextFactory<FamilyBudgetDbContext> dbFactory)
{
    // Factory, not DbContext itself: each method creates a short-lived context (Architecture Decision #7)
    private readonly IDbContextFactory<FamilyBudgetDbContext> _dbFactory = dbFactory;

    // The whole tree of the user's group. Siblings are ordered by Id (creation order).
    public async Task<IReadOnlyList<CategoryNode>> GetTreeAsync(int userId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var groupId = await GetGroupIdAsync(db, userId);

        var rows = await db.Categories
            .Where(c => c.FamilyGroupId == groupId)
            .OrderBy(c => c.Id)
            .Select(c => new { c.Id, c.Name, c.ParentCategoryId })
            .ToListAsync();

        var childrenByParent = rows.ToLookup(r => r.ParentCategoryId);

        IReadOnlyList<CategoryNode> Build(int? parentId) =>
            childrenByParent[parentId]
                .Select(r => new CategoryNode(r.Id, r.Name, Build(r.Id)))
                .ToList();

        return Build(null);
    }

    public async Task<CreateCategoryResult> CreateAsync(int userId, string? name, int? parentId)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return new CreateCategoryResult(CategoryStatus.EmptyName, null);
        }

        await using var db = await _dbFactory.CreateDbContextAsync();

        var groupId = await GetGroupIdAsync(db, userId);

        if (parentId is not null &&
            !await db.Categories.AnyAsync(c => c.Id == parentId && c.FamilyGroupId == groupId))
        {
            return new CreateCategoryResult(CategoryStatus.NotFound, null);
        }

        var category = new Category
        {
            Name = trimmed,
            ParentCategoryId = parentId,
            FamilyGroupId = groupId
        };
        db.Categories.Add(category);
        await db.SaveChangesAsync();

        return new CreateCategoryResult(CategoryStatus.Ok, category.Id);
    }

    public async Task<CategoryStatus> RenameAsync(int userId, int categoryId, string? name)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return CategoryStatus.EmptyName;
        }

        await using var db = await _dbFactory.CreateDbContextAsync();

        var groupId = await GetGroupIdAsync(db, userId);

        var category = await db.Categories
            .SingleOrDefaultAsync(c => c.Id == categoryId && c.FamilyGroupId == groupId);
        if (category is null)
        {
            return CategoryStatus.NotFound;
        }

        category.Name = trimmed;
        await db.SaveChangesAsync();

        return CategoryStatus.Ok;
    }

    // Deletes the category together with its whole subtree (any depth).
    // Products in the deleted categories keep existing: the database sets their
    // CategoryId to NULL (ON DELETE SET NULL). Purchases are not touched.
    public async Task<CategoryStatus> DeleteAsync(int userId, int categoryId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        await using var transaction = await db.Database.BeginTransactionAsync();

        var groupId = await GetGroupIdAsync(db, userId);

        var links = await db.Categories
            .Where(c => c.FamilyGroupId == groupId)
            .Select(c => new { c.Id, c.ParentCategoryId })
            .ToListAsync();

        if (!links.Any(l => l.Id == categoryId))
        {
            return CategoryStatus.NotFound;
        }

        // Collect the subtree breadth-first, starting from the category itself.
        var childrenByParent = links.ToLookup(l => l.ParentCategoryId, l => l.Id);
        var subtree = new List<int>();
        var queue = new Queue<int>();
        queue.Enqueue(categoryId);
        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            subtree.Add(id);
            foreach (var childId in childrenByParent[id])
            {
                queue.Enqueue(childId);
            }
        }

        // One statement for the whole subtree: the Restrict check on the parent link
        // runs at the end of the statement, when the children are already gone too.
        await db.Categories
            .Where(c => subtree.Contains(c.Id))
            .ExecuteDeleteAsync();

        await transaction.CommitAsync();
        return CategoryStatus.Ok;
    }

    private static Task<int> GetGroupIdAsync(FamilyBudgetDbContext db, int userId) =>
        db.Users
            .Where(u => u.Id == userId)
            .Select(u => u.FamilyGroupId)
            .SingleAsync();
}

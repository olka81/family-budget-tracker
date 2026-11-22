using FamilyBudget.Data;
using FamilyBudget.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FamilyBudget.Services;

public class PurchaseService(IDbContextFactory<FamilyBudgetDbContext> dbFactory, TimeProvider timeProvider)
{
    // Factory, not DbContext itself: each method creates a short-lived context (Architecture Decision #7)
    private readonly IDbContextFactory<FamilyBudgetDbContext> _dbFactory = dbFactory;

    // Source of "today" (UTC date); tests pass a fixed one.
    private readonly TimeProvider _timeProvider = timeProvider;

    // The unique index (FamilyGroupId, Name) on Products (Architecture Decision #2).
    private const string ProductNameIndex = "IX_Products_FamilyGroupId_Name";

    // MVP: one currency (Architecture Decision #9).
    private const string Currency = "EUR";

    // Limits of numeric(10,2) and numeric(10,3): 8 and 7 digits before the decimal point.
    private const decimal MaxAbsPrice = 100_000_000m;
    private const decimal MaxQuantity = 10_000_000m;

    // Saves every item as one Purchase row with the shared header fields, all or nothing.
    public async Task<AddPurchasesResult> AddItemsAsync(
        int userId, PurchaseHeader header, IReadOnlyList<PurchaseItem> items)
    {
        try
        {
            return await TryAddItemsAsync(userId, header, items);
        }
        catch (DbUpdateException ex) when (IsProductNameViolation(ex))
        {
            // A concurrent request created one of our new products. The failed transaction was
            // rolled back (PostgreSQL cannot continue it); retry once, the product is now found.
            return await TryAddItemsAsync(userId, header, items);
        }
    }

    // Purchases of the user's group with Date in [from, to], newest first.
    public async Task<IReadOnlyList<PurchaseInfo>> GetForPeriodAsync(int userId, DateOnly from, DateOnly to)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var groupId = await GetGroupIdAsync(db, userId);

        return await Project(db.Purchases
                .Where(p => p.FamilyGroupId == groupId && p.Date >= from && p.Date <= to)
                .OrderByDescending(p => p.Date)
                .ThenByDescending(p => p.Id))
            .ToListAsync();
    }

    public async Task<GetPurchaseResult> GetAsync(int userId, int purchaseId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var groupId = await GetGroupIdAsync(db, userId);

        var purchase = await Project(db.Purchases
                .Where(p => p.Id == purchaseId && p.FamilyGroupId == groupId))
            .SingleOrDefaultAsync();

        return purchase is null
            ? new GetPurchaseResult(PurchaseStatus.NotFound, null)
            : new GetPurchaseResult(PurchaseStatus.Ok, purchase);
    }

    // Every field can be changed; the product is found or created by name, as in AddItemsAsync.
    public async Task<UpdatePurchaseResult> UpdateAsync(
        int userId, int purchaseId, PurchaseHeader header, PurchaseItem item)
    {
        try
        {
            return await TryUpdateAsync(userId, purchaseId, header, item);
        }
        catch (DbUpdateException ex) when (IsProductNameViolation(ex))
        {
            // Same retry rule as in AddItemsAsync.
            return await TryUpdateAsync(userId, purchaseId, header, item);
        }
    }

    // The product and the store of the purchase are not touched.
    public async Task<PurchaseStatus> DeleteAsync(int userId, int purchaseId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var groupId = await GetGroupIdAsync(db, userId);

        var purchase = await FindInGroupAsync(db, purchaseId, groupId);
        if (purchase is null)
        {
            return PurchaseStatus.NotFound;
        }

        db.Purchases.Remove(purchase);
        await db.SaveChangesAsync();

        return PurchaseStatus.Ok;
    }

    // One attempt: validation, find-or-create of products and the insert, in one transaction.
    // A unique violation on a product name propagates to the caller, which may retry.
    private async Task<AddPurchasesResult> TryAddItemsAsync(
        int userId, PurchaseHeader header, IReadOnlyList<PurchaseItem> items)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        await using var transaction = await db.Database.BeginTransactionAsync();

        var groupId = await GetGroupIdAsync(db, userId);

        var checkedHeader = await ValidateHeaderAsync(db, groupId, userId, header);
        var errors = new List<PurchaseError>(checkedHeader.Errors);
        if (items.Count == 0)
        {
            errors.Add(PurchaseError.NoItems);
        }

        var checkedItems = items.Select(CheckItem).ToList();
        var validCategoryIds = await FindCategoriesInGroupAsync(db, groupId, checkedItems);

        var itemErrors = new List<ItemError>();
        for (var i = 0; i < checkedItems.Count; i++)
        {
            itemErrors.AddRange(ItemErrorsOf(checkedItems[i], validCategoryIds).Select(e => new ItemError(i, e)));
        }

        if (errors.Count > 0 || itemErrors.Count > 0)
        {
            // Nothing has been written; disposing the transaction rolls it back.
            return new AddPurchasesResult(PurchaseStatus.Invalid, [], null, false, errors, itemErrors);
        }

        var products = await ResolveProductsAsync(db, groupId, checkedItems);

        var purchases = checkedItems
            .Select(item => new Purchase
            {
                Product = products[item.Name],
                Price = item.Price,
                Quantity = item.Quantity,
                PaymentMethod = header.PaymentMethod,
                Date = checkedHeader.Date,
                StoreId = header.StoreId,
                UserId = checkedHeader.BuyerUserId,
                FamilyGroupId = groupId,
                Currency = Currency,
                Notes = item.Notes
            })
            .ToList();
        db.Purchases.AddRange(purchases);

        // New products and all purchases are inserted by one SaveChanges.
        await db.SaveChangesAsync();
        await transaction.CommitAsync();

        return new AddPurchasesResult(
            PurchaseStatus.Ok,
            purchases.Select(p => p.Id).ToList(),
            checkedHeader.Date,
            checkedHeader.DateCorrected,
            [],
            []);
    }

    private async Task<UpdatePurchaseResult> TryUpdateAsync(
        int userId, int purchaseId, PurchaseHeader header, PurchaseItem item)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        await using var transaction = await db.Database.BeginTransactionAsync();

        var groupId = await GetGroupIdAsync(db, userId);

        var purchase = await FindInGroupAsync(db, purchaseId, groupId);
        if (purchase is null)
        {
            return new UpdatePurchaseResult(PurchaseStatus.NotFound, null, false, []);
        }

        var checkedHeader = await ValidateHeaderAsync(db, groupId, userId, header);
        var checkedItem = CheckItem(item);
        var validCategoryIds = await FindCategoriesInGroupAsync(db, groupId, [checkedItem]);

        var errors = checkedHeader.Errors.Concat(ItemErrorsOf(checkedItem, validCategoryIds)).ToList();
        if (errors.Count > 0)
        {
            return new UpdatePurchaseResult(PurchaseStatus.Invalid, null, false, errors);
        }

        var products = await ResolveProductsAsync(db, groupId, [checkedItem]);

        purchase.Product = products[checkedItem.Name];
        purchase.Price = checkedItem.Price;
        purchase.Quantity = checkedItem.Quantity;
        purchase.PaymentMethod = header.PaymentMethod;
        purchase.Date = checkedHeader.Date;
        purchase.StoreId = header.StoreId;
        purchase.UserId = checkedHeader.BuyerUserId;
        purchase.Notes = checkedItem.Notes;

        await db.SaveChangesAsync();
        await transaction.CommitAsync();

        return new UpdatePurchaseResult(PurchaseStatus.Ok, checkedHeader.Date, checkedHeader.DateCorrected, []);
    }

    private record CheckedHeader(List<PurchaseError> Errors, DateOnly Date, bool DateCorrected, int BuyerUserId);

    // An item after normalization; Errors holds the problems found without the database.
    private record CheckedItem(
        string Name, int? CategoryId, decimal Price, decimal Quantity, string? Notes, List<PurchaseError> Errors);

    private async Task<CheckedHeader> ValidateHeaderAsync(
        FamilyBudgetDbContext db, int groupId, int userId, PurchaseHeader header)
    {
        var errors = new List<PurchaseError>();

        // A date later than tomorrow (UTC) is replaced with tomorrow.
        var tomorrow = DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime).AddDays(1);
        var dateCorrected = header.Date > tomorrow;
        var date = dateCorrected ? tomorrow : header.Date;

        if (header.StoreId is { } storeId
            && !await db.Stores.AnyAsync(s => s.Id == storeId && s.FamilyGroupId == groupId))
        {
            errors.Add(PurchaseError.StoreNotFound);
        }

        // The buyer must be a real member of the same group (Architecture Decision #4).
        var buyerUserId = header.BuyerUserId ?? userId;
        if (header.BuyerUserId is { } buyerId
            && !await db.Users.AnyAsync(u => u.Id == buyerId && u.FamilyGroupId == groupId))
        {
            errors.Add(PurchaseError.BuyerNotInGroup);
        }

        if (!Enum.IsDefined(header.PaymentMethod))
        {
            errors.Add(PurchaseError.InvalidPaymentMethod);
        }

        return new CheckedHeader(errors, date, dateCorrected, buyerUserId);
    }

    // Values are checked, never rounded: a value that does not fit the column is an error.
    private static CheckedItem CheckItem(PurchaseItem item)
    {
        var errors = new List<PurchaseError>();

        var name = NameNormalizer.Normalize(item.ProductName);
        if (name.Length == 0)
        {
            errors.Add(PurchaseError.EmptyProductName);
        }

        // Any sign is allowed: 0 is a free item, a negative price is a discount line.
        if (Math.Abs(item.Price) >= MaxAbsPrice || decimal.Round(item.Price, 2) != item.Price)
        {
            errors.Add(PurchaseError.InvalidPrice);
        }

        if (item.Quantity <= 0 || item.Quantity >= MaxQuantity || decimal.Round(item.Quantity, 3) != item.Quantity)
        {
            errors.Add(PurchaseError.InvalidQuantity);
        }

        var notes = string.IsNullOrWhiteSpace(item.Notes) ? null : item.Notes.Trim();

        return new CheckedItem(name, item.CategoryId, item.Price, item.Quantity, notes, errors);
    }

    // Every given category id is checked, even for an existing product (same as ProductService).
    private static IEnumerable<PurchaseError> ItemErrorsOf(CheckedItem item, HashSet<int> validCategoryIds)
    {
        var errors = new List<PurchaseError>(item.Errors);
        if (item.CategoryId is { } categoryId && !validCategoryIds.Contains(categoryId))
        {
            errors.Add(PurchaseError.CategoryNotFound);
        }

        return errors;
    }

    // The given category ids that belong to the group.
    private static async Task<HashSet<int>> FindCategoriesInGroupAsync(
        FamilyBudgetDbContext db, int groupId, IReadOnlyList<CheckedItem> items)
    {
        var ids = items.Where(i => i.CategoryId is not null).Select(i => i.CategoryId!.Value).Distinct().ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        var found = await db.Categories
            .Where(c => ids.Contains(c.Id) && c.FamilyGroupId == groupId)
            .Select(c => c.Id)
            .ToListAsync();

        return found.ToHashSet();
    }

    // Finds the products of the items by normalized name and adds the missing ones (tracked,
    // inserted by the caller's SaveChanges). The same name twice gives one product; a new
    // product gets the category of the first item with its name. An existing product's
    // category is never changed.
    private static async Task<Dictionary<string, Product>> ResolveProductsAsync(
        FamilyBudgetDbContext db, int groupId, IReadOnlyList<CheckedItem> items)
    {
        var names = items.Select(i => i.Name).Distinct().ToList();

        var products = await db.Products
            .Where(p => p.FamilyGroupId == groupId && names.Contains(p.Name))
            .ToDictionaryAsync(p => p.Name);

        foreach (var item in items)
        {
            if (!products.ContainsKey(item.Name))
            {
                var product = new Product { Name = item.Name, CategoryId = item.CategoryId, FamilyGroupId = groupId };
                db.Products.Add(product);
                products[item.Name] = product;
            }
        }

        return products;
    }

    private static IQueryable<PurchaseInfo> Project(IQueryable<Purchase> purchases) =>
        purchases.Select(p => new PurchaseInfo(
            p.Id,
            p.Date,
            p.ProductId,
            p.Product.Name,
            p.Product.CategoryId,
            p.Product.Category != null ? p.Product.Category.Name : null,
            p.StoreId,
            p.Store != null ? p.Store.Name : null,
            p.UserId,
            p.User.Email,
            p.PaymentMethod,
            p.Price,
            p.Quantity,
            p.Price * p.Quantity,
            p.Notes));

    private static Task<int> GetGroupIdAsync(FamilyBudgetDbContext db, int userId) =>
        db.Users
            .Where(u => u.Id == userId)
            .Select(u => u.FamilyGroupId)
            .SingleAsync();

    private static Task<Purchase?> FindInGroupAsync(FamilyBudgetDbContext db, int purchaseId, int groupId) =>
        db.Purchases.SingleOrDefaultAsync(p => p.Id == purchaseId && p.FamilyGroupId == groupId);

    private static bool IsProductNameViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException pg
        && pg.SqlState == PostgresErrorCodes.UniqueViolation
        && pg.ConstraintName == ProductNameIndex;
}

using FamilyBudget.Models;
using FamilyBudget.Services;

namespace FamilyBudget.Tests;

// A clock fixed at one moment, so "today" and "tomorrow" are deterministic.
internal sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => utcNow;
}

// Each test creates its own groups, users, categories, products, stores and purchases.
public class PurchaseServiceTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    // Late in the UTC day, so a local-time "today" would differ in many time zones.
    private static readonly DateOnly Today = new(2026, 6, 15);
    private static readonly DateOnly Tomorrow = Today.AddDays(1);

    private readonly PurchaseService _service =
        new(fixture.DbFactory, new FixedTimeProvider(new DateTimeOffset(2026, 6, 15, 23, 30, 0, TimeSpan.Zero)));
    private readonly ProductService _productService = new(fixture.DbFactory);
    private readonly StoreService _storeService = new(fixture.DbFactory);
    private readonly TestData _data = new(fixture.DbFactory);

    private async Task<(int GroupId, int UserId)> CreateUserInNewGroupAsync(string groupName)
    {
        var group = await _data.CreateGroupAsync(groupName);
        var user = await _data.CreateUserAsync(group.Id);
        return (group.Id, user.Id);
    }

    private static PurchaseHeader Header(
        DateOnly? date = null, int? storeId = null, int? buyerId = null, PaymentMethod method = PaymentMethod.Card) =>
        new(date ?? Today, storeId, buyerId, method);

    private static PurchaseItem Item(
        string? name = "bread", decimal price = 1.20m, decimal quantity = 1m, int? categoryId = null, string? notes = null) =>
        new(name, categoryId, price, quantity, notes);

    // ---------- Add items ----------

    [Fact]
    public async Task Add_SeveralItems_OneRowPerItem_SharedHeader_GroupAndEur_IdsInOrder()
    {
        var (groupId, userId) = await CreateUserInNewGroupAsync("Group for add several");
        var store = await _data.CreateStoreAsync(groupId, "market");
        var date = Today.AddDays(-3);

        var result = await _service.AddItemsAsync(
            userId,
            Header(date, store.Id, null, PaymentMethod.Cash),
            [Item("milk", 1.99m, 2m, notes: "  2 bottles "), Item("bread", 0.89m, 1.5m), Item("cheese", 4.50m, 0.255m)]);

        Assert.Equal(PurchaseStatus.Ok, result.Status);
        Assert.Equal(date, result.SavedDate);
        Assert.False(result.DateCorrected);
        Assert.Empty(result.Errors);
        Assert.Empty(result.ItemErrors);

        var rows = await _data.GetPurchasesAsync(groupId);
        Assert.Equal(3, rows.Count);
        Assert.Equal(rows.Select(r => r.Id), result.PurchaseIds);
        Assert.All(rows, r =>
        {
            Assert.Equal(date, r.Date);
            Assert.Equal(store.Id, r.StoreId);
            Assert.Equal(userId, r.UserId);
            Assert.Equal(PaymentMethod.Cash, r.PaymentMethod);
            Assert.Equal(groupId, r.FamilyGroupId);
            Assert.Equal("EUR", r.Currency);
        });

        var products = (await _data.GetProductsAsync(groupId)).ToDictionary(p => p.Id, p => p.Name);
        Assert.Equal(["milk", "bread", "cheese"], rows.Select(r => products[r.ProductId]));
        Assert.Equal([1.99m, 0.89m, 4.50m], rows.Select(r => r.Price));
        Assert.Equal([2m, 1.5m, 0.255m], rows.Select(r => r.Quantity));
        Assert.Equal(["2 bottles", null, null], rows.Select(r => r.Notes));
    }

    [Fact]
    public async Task Add_NewNamesNormalized_ExistingProductReusedIgnoringCaseAndSpaces()
    {
        var (groupId, userId) = await CreateUserInNewGroupAsync("Group for normalization");
        var existing = await _data.CreateProductAsync(groupId, "молоко 3,2%", null);

        var result = await _service.AddItemsAsync(
            userId, Header(), [Item("  МОЛОКО   3,2% "), Item("  Black \t Tea ")]);

        Assert.Equal(PurchaseStatus.Ok, result.Status);
        var products = await _data.GetProductsAsync(groupId);
        Assert.Equal(["молоко 3,2%", "black tea"], products.Select(p => p.Name));
        var rows = await _data.GetPurchasesAsync(groupId);
        Assert.Equal(existing.Id, rows[0].ProductId);
        Assert.Equal(products[1].Id, rows[1].ProductId);
    }

    [Fact]
    public async Task Add_SameNewNameTwice_OneProduct_FirstItemCategory()
    {
        var (groupId, userId) = await CreateUserInNewGroupAsync("Group for same name twice");
        var first = await _data.CreateCategoryAsync(groupId, "First");
        var second = await _data.CreateCategoryAsync(groupId, "Second");

        var result = await _service.AddItemsAsync(
            userId, Header(), [Item("Apples", categoryId: first.Id), Item("APPLES ", -0.30m, categoryId: second.Id)]);

        Assert.Equal(PurchaseStatus.Ok, result.Status);
        var product = Assert.Single(await _data.GetProductsAsync(groupId));
        Assert.Equal(new ProductRow(product.Id, "apples", first.Id, groupId), product);
        Assert.All(await _data.GetPurchasesAsync(groupId), r => Assert.Equal(product.Id, r.ProductId));
    }

    [Fact]
    public async Task Add_CategoryForExistingProduct_Ignored()
    {
        var (groupId, userId) = await CreateUserInNewGroupAsync("Group for existing category");
        var original = await _data.CreateCategoryAsync(groupId, "Original");
        var other = await _data.CreateCategoryAsync(groupId, "Other");
        var product = await _data.CreateProductAsync(groupId, "rice", original.Id);
        var uncategorized = await _data.CreateProductAsync(groupId, "salt", null);

        var result = await _service.AddItemsAsync(
            userId, Header(), [Item("Rice", categoryId: other.Id), Item("salt", categoryId: other.Id)]);

        Assert.Equal(PurchaseStatus.Ok, result.Status);
        Assert.Equal(original.Id, (await _data.FindProductAsync(product.Id))!.CategoryId);
        Assert.Null((await _data.FindProductAsync(uncategorized.Id))!.CategoryId);
        Assert.Equal(2, (await _data.GetProductsAsync(groupId)).Count);
    }

    [Fact]
    public async Task Add_NegativeAndZeroPrice_SavedAsGiven()
    {
        var (groupId, userId) = await CreateUserInNewGroupAsync("Group for discounts");

        var result = await _service.AddItemsAsync(
            userId, Header(), [Item("coffee", 5.00m), Item("coffee", -1.25m), Item("free sample", 0m)]);

        Assert.Equal(PurchaseStatus.Ok, result.Status);
        Assert.Equal([5.00m, -1.25m, 0m], (await _data.GetPurchasesAsync(groupId)).Select(r => r.Price));
    }

    [Fact]
    public async Task Add_BuyerDefaultsToCurrentUser()
    {
        var (groupId, userId) = await CreateUserInNewGroupAsync("Group for default buyer");
        await _data.CreateUserAsync(groupId);

        var result = await _service.AddItemsAsync(userId, Header(buyerId: null), [Item()]);

        Assert.Equal(PurchaseStatus.Ok, result.Status);
        Assert.Equal(userId, Assert.Single(await _data.GetPurchasesAsync(groupId)).UserId);
    }

    [Fact]
    public async Task Add_OtherMemberAsBuyer_Accepted()
    {
        var (groupId, userId) = await CreateUserInNewGroupAsync("Group for other buyer");
        var member = await _data.CreateUserAsync(groupId);

        var result = await _service.AddItemsAsync(userId, Header(buyerId: member.Id), [Item()]);

        Assert.Equal(PurchaseStatus.Ok, result.Status);
        Assert.Equal(member.Id, Assert.Single(await _data.GetPurchasesAsync(groupId)).UserId);
    }

    [Fact]
    public async Task Add_BuyerFromOtherGroup_BuyerNotInGroup_NothingSaved()
    {
        var (groupId, userId) = await CreateUserInNewGroupAsync("Group for foreign buyer");
        var (_, strangerId) = await CreateUserInNewGroupAsync("Stranger's group");

        var result = await _service.AddItemsAsync(userId, Header(buyerId: strangerId), [Item("new thing")]);

        Assert.Equal(PurchaseStatus.Invalid, result.Status);
        Assert.Equal([PurchaseError.BuyerNotInGroup], result.Errors);
        Assert.Empty(result.ItemErrors);
        Assert.Empty(result.PurchaseIds);
        Assert.Empty(await _data.GetPurchasesAsync(groupId));
        Assert.Empty(await _data.GetProductsAsync(groupId));
    }

    [Fact]
    public async Task Add_StoreFromOtherGroup_StoreNotFound_NothingSaved()
    {
        var (groupId, userId) = await CreateUserInNewGroupAsync("Group for foreign store");
        var otherGroup = await _data.CreateGroupAsync("Other group with store");
        var foreignStore = await _data.CreateStoreAsync(otherGroup.Id, "their shop");

        var result = await _service.AddItemsAsync(userId, Header(storeId: foreignStore.Id), [Item("new thing")]);

        Assert.Equal(PurchaseStatus.Invalid, result.Status);
        Assert.Equal([PurchaseError.StoreNotFound], result.Errors);
        Assert.Empty(await _data.GetPurchasesAsync(groupId));
        Assert.Empty(await _data.GetProductsAsync(groupId));
    }

    [Fact]
    public async Task Add_CategoryFromOtherGroup_CategoryNotFoundWithIndex_NothingSaved()
    {
        var (groupId, userId) = await CreateUserInNewGroupAsync("Group for foreign category");
        var otherGroup = await _data.CreateGroupAsync("Other group with category");
        var foreignCategory = await _data.CreateCategoryAsync(otherGroup.Id, "Theirs");

        var result = await _service.AddItemsAsync(
            userId, Header(), [Item("bread"), Item("butter", categoryId: foreignCategory.Id)]);

        Assert.Equal(PurchaseStatus.Invalid, result.Status);
        Assert.Empty(result.Errors);
        Assert.Equal([new ItemError(1, PurchaseError.CategoryNotFound)], result.ItemErrors);
        Assert.Empty(await _data.GetPurchasesAsync(groupId));
        Assert.Empty(await _data.GetProductsAsync(groupId));
    }

    [Fact]
    public async Task Add_OneValidTwoInvalid_NothingSaved_BothReportedWithIndexes()
    {
        var (groupId, userId) = await CreateUserInNewGroupAsync("Group for all or nothing");

        var result = await _service.AddItemsAsync(
            userId, Header(), [Item("bread"), Item("   "), Item("milk", quantity: 0m)]);

        Assert.Equal(PurchaseStatus.Invalid, result.Status);
        Assert.Equal(
            [new ItemError(1, PurchaseError.EmptyProductName), new ItemError(2, PurchaseError.InvalidQuantity)],
            result.ItemErrors);
        Assert.Null(result.SavedDate);
        Assert.Empty(await _data.GetPurchasesAsync(groupId));
        Assert.Empty(await _data.GetProductsAsync(groupId));
    }

    [Fact]
    public async Task Add_ItemWithSeveralProblems_EachReportedWithSameIndex()
    {
        var (groupId, userId) = await CreateUserInNewGroupAsync("Group for several problems");

        var result = await _service.AddItemsAsync(userId, Header(), [Item("", 0.001m, -1m)]);

        Assert.Equal(PurchaseStatus.Invalid, result.Status);
        Assert.Equal(
            [
                new ItemError(0, PurchaseError.EmptyProductName),
                new ItemError(0, PurchaseError.InvalidPrice),
                new ItemError(0, PurchaseError.InvalidQuantity)
            ],
            result.ItemErrors);
        Assert.Empty(await _data.GetPurchasesAsync(groupId));
    }

    [Theory]
    [InlineData("1.234")]
    [InlineData("100000000")]
    [InlineData("-100000000")]
    public async Task Add_InvalidPrice(string price)
    {
        var (groupId, userId) = await CreateUserInNewGroupAsync("Group for invalid price");

        var result = await _service.AddItemsAsync(
            userId, Header(), [Item(price: decimal.Parse(price, System.Globalization.CultureInfo.InvariantCulture))]);

        Assert.Equal(PurchaseStatus.Invalid, result.Status);
        Assert.Equal([new ItemError(0, PurchaseError.InvalidPrice)], result.ItemErrors);
        Assert.Empty(await _data.GetPurchasesAsync(groupId));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("1.2345")]
    [InlineData("10000000")]
    public async Task Add_InvalidQuantity(string quantity)
    {
        var (groupId, userId) = await CreateUserInNewGroupAsync("Group for invalid quantity");

        var result = await _service.AddItemsAsync(
            userId, Header(), [Item(quantity: decimal.Parse(quantity, System.Globalization.CultureInfo.InvariantCulture))]);

        Assert.Equal(PurchaseStatus.Invalid, result.Status);
        Assert.Equal([new ItemError(0, PurchaseError.InvalidQuantity)], result.ItemErrors);
        Assert.Empty(await _data.GetPurchasesAsync(groupId));
    }

    [Fact]
    public async Task Add_LimitValues_Saved()
    {
        var (groupId, userId) = await CreateUserInNewGroupAsync("Group for limit values");

        var result = await _service.AddItemsAsync(
            userId, Header(), [Item("max", 99_999_999.99m, 9_999_999.999m), Item("min", -99_999_999.99m, 0.001m)]);

        Assert.Equal(PurchaseStatus.Ok, result.Status);
        var rows = await _data.GetPurchasesAsync(groupId);
        Assert.Equal([99_999_999.99m, -99_999_999.99m], rows.Select(r => r.Price));
        Assert.Equal([9_999_999.999m, 0.001m], rows.Select(r => r.Quantity));
    }

    [Fact]
    public async Task Add_InvalidPaymentMethod()
    {
        var (groupId, userId) = await CreateUserInNewGroupAsync("Group for invalid payment method");

        var result = await _service.AddItemsAsync(userId, Header(method: (PaymentMethod)7), [Item()]);

        Assert.Equal(PurchaseStatus.Invalid, result.Status);
        Assert.Equal([PurchaseError.InvalidPaymentMethod], result.Errors);
        Assert.Empty(await _data.GetPurchasesAsync(groupId));
    }

    [Fact]
    public async Task Add_EmptyList_NoItems()
    {
        var (groupId, userId) = await CreateUserInNewGroupAsync("Group for no items");

        var result = await _service.AddItemsAsync(userId, Header(), []);

        Assert.Equal(PurchaseStatus.Invalid, result.Status);
        Assert.Equal([PurchaseError.NoItems], result.Errors);
        Assert.Empty(result.ItemErrors);
        Assert.Empty(await _data.GetPurchasesAsync(groupId));
    }

    public static TheoryData<int, int, bool> DateCases => new()
    {
        // days from today given, days from today saved, corrected
        { 1, 1, false },
        { 2, 1, true },
        { 365, 1, true },
        { 0, 0, false },
        { -40, -40, false },
    };

    [Theory]
    [MemberData(nameof(DateCases))]
    public async Task Add_Date(int givenOffset, int savedOffset, bool corrected)
    {
        var (groupId, userId) = await CreateUserInNewGroupAsync("Group for dates");

        var result = await _service.AddItemsAsync(userId, Header(Today.AddDays(givenOffset)), [Item()]);

        Assert.Equal(PurchaseStatus.Ok, result.Status);
        Assert.Equal(Today.AddDays(savedOffset), result.SavedDate);
        Assert.Equal(corrected, result.DateCorrected);
        Assert.Equal(Today.AddDays(savedOffset), Assert.Single(await _data.GetPurchasesAsync(groupId)).Date);
    }

    // ---------- Read ----------

    [Fact]
    public async Task GetForPeriod_OwnGroupOnly_InclusiveRange_Ordered_LineTotal_NamesAndNulls()
    {
        var (groupId, userId) = await CreateUserInNewGroupAsync("Group for period");
        var member = await _data.CreateUserAsync(groupId);
        var category = await _data.CreateCategoryAsync(groupId, "Dairy");
        await _data.CreateProductAsync(groupId, "yogurt", category.Id);
        var store = await _data.CreateStoreAsync(groupId, "corner shop");
        var from = new DateOnly(2026, 5, 1);
        var to = new DateOnly(2026, 5, 31);

        // Outside the range on both sides.
        await _service.AddItemsAsync(userId, Header(from.AddDays(-1)), [Item("outside")]);
        await _service.AddItemsAsync(userId, Header(to.AddDays(1)), [Item("outside")]);
        // Inside: two on the last day (order by id desc), one on the first day.
        var first = await _service.AddItemsAsync(
            userId, Header(from), [Item("no category", 2.00m, 1m)]);
        var last = await _service.AddItemsAsync(
            userId,
            Header(to, store.Id, member.Id, PaymentMethod.Cash),
            [Item("yogurt", 0.85m, 3m, notes: "promo"), Item("cheese", 10.00m, 0.255m)]);
        // Another group's purchase in the same range.
        var (_, strangerId) = await CreateUserInNewGroupAsync("Stranger group for period");
        await _service.AddItemsAsync(strangerId, Header(to), [Item("theirs")]);

        var rows = await _service.GetForPeriodAsync(userId, from, to);

        var products = (await _data.GetProductsAsync(groupId)).ToDictionary(p => p.Name, p => p.Id);
        Assert.Equal(
            [
                new PurchaseInfo(last.PurchaseIds[1], to, products["cheese"], "cheese", null, null,
                    store.Id, "corner shop", member.Id, member.Email, PaymentMethod.Cash, 10.00m, 0.255m, 2.55m, null),
                new PurchaseInfo(last.PurchaseIds[0], to, products["yogurt"], "yogurt", category.Id, "Dairy",
                    store.Id, "corner shop", member.Id, member.Email, PaymentMethod.Cash, 0.85m, 3m, 2.55m, "promo"),
                new PurchaseInfo(first.PurchaseIds[0], from, products["no category"], "no category", null, null,
                    null, null, userId, (await _data.FindUserAsync(userId))!.Email, PaymentMethod.Card,
                    2.00m, 1m, 2.00m, null)
            ],
            rows);
    }

    [Fact]
    public async Task Get_OwnPurchase_Returned()
    {
        var (groupId, userId) = await CreateUserInNewGroupAsync("Group for get one");
        var store = await _data.CreateStoreAsync(groupId, "bakery");
        var added = await _service.AddItemsAsync(userId, Header(Today, store.Id), [Item("bun", 0.60m, 4m, notes: "fresh")]);

        var result = await _service.GetAsync(userId, added.PurchaseIds[0]);

        Assert.Equal(PurchaseStatus.Ok, result.Status);
        var product = Assert.Single(await _data.GetProductsAsync(groupId));
        Assert.Equal(
            new PurchaseInfo(added.PurchaseIds[0], Today, product.Id, "bun", null, null, store.Id, "bakery",
                userId, (await _data.FindUserAsync(userId))!.Email, PaymentMethod.Card, 0.60m, 4m, 2.40m, "fresh"),
            result.Purchase);
    }

    [Fact]
    public async Task Get_OtherGroupPurchase_NotFound()
    {
        var (_, userId) = await CreateUserInNewGroupAsync("Group for get foreign");
        var (_, strangerId) = await CreateUserInNewGroupAsync("Stranger group for get");
        var theirs = await _service.AddItemsAsync(strangerId, Header(), [Item()]);

        var result = await _service.GetAsync(userId, theirs.PurchaseIds[0]);

        Assert.Equal(new GetPurchaseResult(PurchaseStatus.NotFound, null), result);
    }

    // ---------- Update ----------

    [Fact]
    public async Task Update_AllFields_Stored_NewProductCreated()
    {
        var (groupId, userId) = await CreateUserInNewGroupAsync("Group for update");
        var member = await _data.CreateUserAsync(groupId);
        var oldStore = await _data.CreateStoreAsync(groupId, "old shop");
        var newStore = await _data.CreateStoreAsync(groupId, "new shop");
        var category = await _data.CreateCategoryAsync(groupId, "Snacks");
        var added = await _service.AddItemsAsync(
            userId, Header(Today.AddDays(-5), oldStore.Id), [Item("chips", 1.00m, 1m, notes: "old")]);
        var id = added.PurchaseIds[0];

        var result = await _service.UpdateAsync(
            userId, id,
            Header(Today.AddDays(-2), newStore.Id, member.Id, PaymentMethod.Cash),
            Item("  Pretzels ", -0.50m, 2.5m, category.Id, " new note "));

        Assert.Equal(new UpdatePurchaseResult(PurchaseStatus.Ok, Today.AddDays(-2), false, []), result);
        var products = await _data.GetProductsAsync(groupId);
        Assert.Equal(2, products.Count);
        var pretzels = products.Single(p => p.Name == "pretzels");
        Assert.Equal(category.Id, pretzels.CategoryId);

        var row = (await _data.FindPurchaseAsync(id))!;
        Assert.Equal(pretzels.Id, row.ProductId);
        Assert.Equal(-0.50m, row.Price);
        Assert.Equal(2.5m, row.Quantity);
        Assert.Equal(Today.AddDays(-2), row.Date);
        Assert.Equal(newStore.Id, row.StoreId);
        Assert.Equal(member.Id, row.UserId);
        Assert.Equal(PaymentMethod.Cash, row.PaymentMethod);
        Assert.Equal("new note", row.Notes);
        Assert.Equal(groupId, row.FamilyGroupId);
        Assert.Equal("EUR", row.Currency);
    }

    [Fact]
    public async Task Update_ClearStoreAndNotes_ExistingProductCategoryUnchanged()
    {
        var (groupId, userId) = await CreateUserInNewGroupAsync("Group for update clear");
        var store = await _data.CreateStoreAsync(groupId, "shop");
        var category = await _data.CreateCategoryAsync(groupId, "Kept");
        var other = await _data.CreateCategoryAsync(groupId, "Ignored");
        var product = await _data.CreateProductAsync(groupId, "tea", category.Id);
        var added = await _service.AddItemsAsync(userId, Header(Today, store.Id), [Item("tea", notes: "x")]);

        var result = await _service.UpdateAsync(
            userId, added.PurchaseIds[0], Header(Today), Item("TEA", categoryId: other.Id, notes: "   "));

        Assert.Equal(PurchaseStatus.Ok, result.Status);
        var row = (await _data.FindPurchaseAsync(added.PurchaseIds[0]))!;
        Assert.Null(row.StoreId);
        Assert.Null(row.Notes);
        Assert.Equal(product.Id, row.ProductId);
        Assert.Equal(category.Id, (await _data.FindProductAsync(product.Id))!.CategoryId);
    }

    [Fact]
    public async Task Update_InvalidData_ErrorAndUnchanged()
    {
        var (groupId, userId) = await CreateUserInNewGroupAsync("Group for invalid update");
        var (_, strangerId) = await CreateUserInNewGroupAsync("Stranger group for invalid update");
        var added = await _service.AddItemsAsync(userId, Header(Today.AddDays(-1)), [Item("bread", 1.20m, 1m)]);
        var id = added.PurchaseIds[0];
        var before = await _data.FindPurchaseAsync(id);

        var result = await _service.UpdateAsync(
            userId, id, Header(Today, buyerId: strangerId), Item("new product", 1.234m, 0m));

        Assert.Equal(PurchaseStatus.Invalid, result.Status);
        Assert.Null(result.SavedDate);
        Assert.Equal(
            [PurchaseError.BuyerNotInGroup, PurchaseError.InvalidPrice, PurchaseError.InvalidQuantity],
            result.Errors);
        Assert.Equivalent(before, await _data.FindPurchaseAsync(id));
        Assert.Single(await _data.GetProductsAsync(groupId));
    }

    [Fact]
    public async Task Update_OtherGroupPurchase_NotFound()
    {
        var (_, userId) = await CreateUserInNewGroupAsync("Group for update foreign");
        var (_, strangerId) = await CreateUserInNewGroupAsync("Stranger group for update");
        var theirs = await _service.AddItemsAsync(strangerId, Header(), [Item("bread", 1.20m)]);
        var before = await _data.FindPurchaseAsync(theirs.PurchaseIds[0]);

        var result = await _service.UpdateAsync(userId, theirs.PurchaseIds[0], Header(), Item("mine", 9.99m));

        Assert.Equal(new UpdatePurchaseResult(PurchaseStatus.NotFound, null, false, []), result);
        Assert.Equivalent(before, await _data.FindPurchaseAsync(theirs.PurchaseIds[0]));
    }

    [Fact]
    public async Task Update_FutureDate_CorrectedToTomorrow()
    {
        var (_, userId) = await CreateUserInNewGroupAsync("Group for update date");
        var added = await _service.AddItemsAsync(userId, Header(Today.AddDays(-1)), [Item()]);

        var result = await _service.UpdateAsync(userId, added.PurchaseIds[0], Header(Today.AddDays(10)), Item());

        Assert.Equal(new UpdatePurchaseResult(PurchaseStatus.Ok, Tomorrow, true, []), result);
        Assert.Equal(Tomorrow, (await _data.FindPurchaseAsync(added.PurchaseIds[0]))!.Date);
    }

    // ---------- Delete ----------

    [Fact]
    public async Task Delete_PurchaseGone_ProductAndStoreRemain()
    {
        var (groupId, userId) = await CreateUserInNewGroupAsync("Group for delete");
        var store = await _data.CreateStoreAsync(groupId, "kiosk");
        var added = await _service.AddItemsAsync(userId, Header(storeId: store.Id), [Item("gum")]);
        var productId = (await _data.FindPurchaseAsync(added.PurchaseIds[0]))!.ProductId;

        var status = await _service.DeleteAsync(userId, added.PurchaseIds[0]);

        Assert.Equal(PurchaseStatus.Ok, status);
        Assert.Null(await _data.FindPurchaseAsync(added.PurchaseIds[0]));
        Assert.NotNull(await _data.FindProductAsync(productId));
        Assert.NotNull(await _data.FindStoreAsync(store.Id));
    }

    [Fact]
    public async Task Delete_OtherGroupPurchase_NotFound_NothingDeleted()
    {
        var (_, userId) = await CreateUserInNewGroupAsync("Group for delete foreign");
        var (_, strangerId) = await CreateUserInNewGroupAsync("Stranger group for delete");
        var theirs = await _service.AddItemsAsync(strangerId, Header(), [Item()]);

        var status = await _service.DeleteAsync(userId, theirs.PurchaseIds[0]);

        Assert.Equal(PurchaseStatus.NotFound, status);
        Assert.NotNull(await _data.FindPurchaseAsync(theirs.PurchaseIds[0]));
    }

    // ---------- Interaction with other services ----------

    [Fact]
    public async Task ProductDelete_ProductWithPurchaseViaService_InUse()
    {
        var (groupId, userId) = await CreateUserInNewGroupAsync("Group for product in use");
        await _service.AddItemsAsync(userId, Header(), [Item("flour")]);
        var product = Assert.Single(await _data.GetProductsAsync(groupId));

        var status = await _productService.DeleteAsync(userId, product.Id);

        Assert.Equal(ProductStatus.InUse, status);
        Assert.NotNull(await _data.FindProductAsync(product.Id));
    }

    [Fact]
    public async Task StoreDelete_StoreUsedByPurchase_PurchaseKeptWithNullStore()
    {
        var (groupId, userId) = await CreateUserInNewGroupAsync("Group for store delete");
        var store = await _data.CreateStoreAsync(groupId, "closing shop");
        var added = await _service.AddItemsAsync(userId, Header(storeId: store.Id), [Item()]);

        var status = await _storeService.DeleteAsync(userId, store.Id);

        Assert.Equal(StoreStatus.Ok, status);
        var row = await _data.FindPurchaseAsync(added.PurchaseIds[0]);
        Assert.NotNull(row);
        Assert.Null(row.StoreId);
    }

    // ---------- Concurrency (smoke test) ----------

    [Fact]
    public async Task Add_ParallelCallsSameNewProduct_OneProduct_AllPurchasesPointToIt()
    {
        var (groupId, userId) = await CreateUserInNewGroupAsync("Group for parallel adds");

        var results = await Task.WhenAll(
            Enumerable.Range(0, 10).Select(_ => _service.AddItemsAsync(userId, Header(), [Item("Sugar")])));

        Assert.All(results, r => Assert.Equal(PurchaseStatus.Ok, r.Status));
        var product = Assert.Single(await _data.GetProductsAsync(groupId));
        Assert.Equal("sugar", product.Name);
        var rows = await _data.GetPurchasesAsync(groupId);
        Assert.Equal(10, rows.Count);
        Assert.All(rows, r => Assert.Equal(product.Id, r.ProductId));
    }
}

using FamilyBudget.Services;

namespace FamilyBudget.Tests;

// JoinAsync deletes the joiner's old personal group and relies on the database cascade
// for its data. These tests check that cascade with products and stores in the group.
public class JoinCascadeTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    private readonly FamilyGroupService _groupService = new(fixture.DbFactory);
    private readonly TestData _data = new(fixture.DbFactory);

    [Fact]
    public async Task Join_SoleMemberWithProductsAndStores_OldDataDeleted_TargetUnchanged()
    {
        // Target group with its own data.
        var token = Guid.NewGuid();
        var targetGroup = await _data.CreateGroupAsync("Target group with data", token);
        await _data.CreateUserAsync(targetGroup.Id);
        var targetCategory = await _data.CreateCategoryAsync(targetGroup.Id, "Target category");
        await _data.CreateProductAsync(targetGroup.Id, "target product", targetCategory.Id);
        await _data.CreateStoreAsync(targetGroup.Id, "target store");
        var targetCategoriesBefore = await _data.GetCategoriesAsync(targetGroup.Id);
        var targetProductsBefore = await _data.GetProductsAsync(targetGroup.Id);
        var targetStoresBefore = await _data.GetStoresAsync(targetGroup.Id);

        // Joiner's personal group: default (nested) categories, products with categories, stores,
        // no purchases.
        var personalGroup = await _data.CreateGroupWithDefaultCategoriesAsync("Personal group with products");
        var joiner = await _data.CreateUserAsync(personalGroup.Id);
        var personalCategories = await _data.GetCategoriesAsync(personalGroup.Id);
        var leafCategory = personalCategories.First(c => c.ParentId is not null);
        var topCategory = personalCategories.First(c => c.ParentId is null);
        await _data.CreateProductAsync(personalGroup.Id, "kefir", leafCategory.Id);
        await _data.CreateProductAsync(personalGroup.Id, "rent", topCategory.Id);
        await _data.CreateProductAsync(personalGroup.Id, "no category", null);
        await _data.CreateStoreAsync(personalGroup.Id, "corner shop");
        await _data.CreateStoreAsync(personalGroup.Id, "market");

        var status = await _groupService.JoinAsync(joiner.Id, token);

        Assert.Equal(JoinStatus.Ok, status);
        Assert.Null(await _data.FindGroupAsync(personalGroup.Id));
        Assert.Empty(await _data.GetProductsAsync(personalGroup.Id));
        Assert.Empty(await _data.GetStoresAsync(personalGroup.Id));
        Assert.Empty(await _data.GetCategoriesAsync(personalGroup.Id));
        Assert.Equal(targetCategoriesBefore, await _data.GetCategoriesAsync(targetGroup.Id));
        Assert.Equal(targetProductsBefore, await _data.GetProductsAsync(targetGroup.Id));
        Assert.Equal(targetStoresBefore, await _data.GetStoresAsync(targetGroup.Id));
    }

    // Purchase -> Product and Purchase -> User are Restrict; the purchases of the old group
    // must still go with the group cascade.
    [Fact]
    public async Task Join_SoleMemberWithPurchases_OldDataDeleted_TargetUnchanged()
    {
        var purchaseService = new PurchaseService(fixture.DbFactory, TimeProvider.System);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // Target group with its own purchase.
        var token = Guid.NewGuid();
        var targetGroup = await _data.CreateGroupAsync("Target group with purchases", token);
        var targetUser = await _data.CreateUserAsync(targetGroup.Id);
        var targetCategory = await _data.CreateCategoryAsync(targetGroup.Id, "Target category");
        var targetStore = await _data.CreateStoreAsync(targetGroup.Id, "target store");
        await purchaseService.AddItemsAsync(
            targetUser.Id,
            new PurchaseHeader(today, targetStore.Id, null, Models.PaymentMethod.Card),
            [new PurchaseItem("target product", targetCategory.Id, 2.00m, 1m, null)]);
        var targetCategoriesBefore = await _data.GetCategoriesAsync(targetGroup.Id);
        var targetProductsBefore = await _data.GetProductsAsync(targetGroup.Id);
        var targetStoresBefore = await _data.GetStoresAsync(targetGroup.Id);
        var targetPurchaseIdsBefore = (await _data.GetPurchasesAsync(targetGroup.Id)).Select(p => p.Id).ToList();

        // Joiner's personal group: nested categories, products, a store and purchases.
        var personalGroup = await _data.CreateGroupWithDefaultCategoriesAsync("Personal group with purchases");
        var joiner = await _data.CreateUserAsync(personalGroup.Id);
        var leafCategory = (await _data.GetCategoriesAsync(personalGroup.Id)).First(c => c.ParentId is not null);
        var store = await _data.CreateStoreAsync(personalGroup.Id, "corner shop");
        var added = await purchaseService.AddItemsAsync(
            joiner.Id,
            new PurchaseHeader(today, store.Id, null, Models.PaymentMethod.Cash),
            [
                new PurchaseItem("kefir", leafCategory.Id, 1.10m, 2m, null),
                new PurchaseItem("discount", null, -0.20m, 1m, null)
            ]);
        Assert.Equal(PurchaseStatus.Ok, added.Status);

        var status = await _groupService.JoinAsync(joiner.Id, token);

        Assert.Equal(JoinStatus.Ok, status);
        Assert.Null(await _data.FindGroupAsync(personalGroup.Id));
        foreach (var id in added.PurchaseIds)
        {
            Assert.Null(await _data.FindPurchaseAsync(id));
        }
        Assert.Empty(await _data.GetPurchasesAsync(personalGroup.Id));
        Assert.Empty(await _data.GetProductsAsync(personalGroup.Id));
        Assert.Empty(await _data.GetStoresAsync(personalGroup.Id));
        Assert.Empty(await _data.GetCategoriesAsync(personalGroup.Id));
        Assert.Equal(targetGroup.Id, (await _data.FindUserAsync(joiner.Id))!.FamilyGroupId);
        Assert.Equal(targetCategoriesBefore, await _data.GetCategoriesAsync(targetGroup.Id));
        Assert.Equal(targetProductsBefore, await _data.GetProductsAsync(targetGroup.Id));
        Assert.Equal(targetStoresBefore, await _data.GetStoresAsync(targetGroup.Id));
        Assert.Equal(
            targetPurchaseIdsBefore, (await _data.GetPurchasesAsync(targetGroup.Id)).Select(p => p.Id).ToList());
    }
}

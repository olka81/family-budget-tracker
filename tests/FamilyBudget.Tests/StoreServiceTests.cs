using FamilyBudget.Services;

namespace FamilyBudget.Tests;

// Each test creates its own groups, users and stores.
public class StoreServiceTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    private readonly StoreService _service = new(fixture.DbFactory);
    private readonly TestData _data = new(fixture.DbFactory);

    private async Task<(int GroupId, int UserId)> CreateUserInNewGroupAsync(string groupName)
    {
        var group = await _data.CreateGroupAsync(groupName);
        var user = await _data.CreateUserAsync(group.Id);
        return (group.Id, user.Id);
    }

    // ---------- Create ----------

    [Fact]
    public async Task Create_StoresNormalizedName()
    {
        var (groupId, userId) = await CreateUserInNewGroupAsync("Group for store create");

        var result = await _service.CreateAsync(userId, "  Lidl \u00A0 Sofia ");

        Assert.Equal(StoreStatus.Ok, result.Status);
        var row = Assert.Single(await _data.GetStoresAsync(groupId));
        Assert.Equal(new StoreRow(result.StoreId!.Value, "lidl sofia", groupId), row);
    }

    [Fact]
    public async Task Create_DuplicateName_BothCreated()
    {
        var (groupId, userId) = await CreateUserInNewGroupAsync("Group for duplicate stores");

        var first = await _service.CreateAsync(userId, "Billa");
        var second = await _service.CreateAsync(userId, "BILLA");

        Assert.Equal(StoreStatus.Ok, first.Status);
        Assert.Equal(StoreStatus.Ok, second.Status);
        Assert.NotEqual(first.StoreId, second.StoreId);
        var rows = await _data.GetStoresAsync(groupId);
        Assert.Equal(2, rows.Count(r => r.Name == "billa"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\u00A0")]
    public async Task Create_EmptyName_ReturnsEmptyName(string name)
    {
        var (groupId, userId) = await CreateUserInNewGroupAsync("Group for empty store name");

        var result = await _service.CreateAsync(userId, name);

        Assert.Equal(new CreateStoreResult(StoreStatus.EmptyName, null), result);
        Assert.Empty(await _data.GetStoresAsync(groupId));
    }

    // ---------- Get all ----------

    [Fact]
    public async Task GetAll_ReturnsOnlyOwnGroupStores()
    {
        var (groupId, userId) = await CreateUserInNewGroupAsync("Group for store list");
        var kaufland = await _data.CreateStoreAsync(groupId, "kaufland");
        var billaOlder = await _data.CreateStoreAsync(groupId, "billa");
        var billaNewer = await _data.CreateStoreAsync(groupId, "billa");
        var otherGroup = await _data.CreateGroupAsync("Other group with stores");
        await _data.CreateStoreAsync(otherGroup.Id, "aldi");

        var stores = await _service.GetAllAsync(userId);

        // Ordered by name, then id.
        Assert.Equal(
            [
                new StoreInfo(billaOlder.Id, "billa"),
                new StoreInfo(billaNewer.Id, "billa"),
                new StoreInfo(kaufland.Id, "kaufland")
            ],
            stores);
    }

    // ---------- Rename ----------

    [Fact]
    public async Task Rename_StoresNormalizedName()
    {
        var (groupId, userId) = await CreateUserInNewGroupAsync("Group for store rename");
        var store = await _data.CreateStoreAsync(groupId, "old store");

        var status = await _service.RenameAsync(userId, store.Id, " Fantastico\tCentre ");

        Assert.Equal(StoreStatus.Ok, status);
        Assert.Equal("fantastico centre", Assert.Single(await _data.GetStoresAsync(groupId)).Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Rename_EmptyName_ReturnsEmptyName(string name)
    {
        var (groupId, userId) = await CreateUserInNewGroupAsync("Group for empty store rename");
        var store = await _data.CreateStoreAsync(groupId, "keep me");

        var status = await _service.RenameAsync(userId, store.Id, name);

        Assert.Equal(StoreStatus.EmptyName, status);
        Assert.Equal("keep me", Assert.Single(await _data.GetStoresAsync(groupId)).Name);
    }

    [Fact]
    public async Task Rename_OtherGroupStore_ReturnsNotFound_Unchanged()
    {
        var (_, userId) = await CreateUserInNewGroupAsync("Group renaming foreign store");
        var otherGroup = await _data.CreateGroupAsync("Group owning the store");
        var foreign = await _data.CreateStoreAsync(otherGroup.Id, "foreign");

        var status = await _service.RenameAsync(userId, foreign.Id, "hacked");

        Assert.Equal(StoreStatus.NotFound, status);
        Assert.Equal("foreign", Assert.Single(await _data.GetStoresAsync(otherGroup.Id)).Name);
    }

    // ---------- Delete ----------

    [Fact]
    public async Task Delete_WithPurchase_StoreDeleted_PurchaseKeptWithNullStore()
    {
        var (groupId, userId) = await CreateUserInNewGroupAsync("Group for store delete");
        var store = await _data.CreateStoreAsync(groupId, "closed store");
        var keep = await _data.CreateStoreAsync(groupId, "open store");
        var product = await _data.CreateProductAsync(groupId, "bread", null);
        var purchase = await _data.CreatePurchaseAsync(groupId, userId, product.Id, store.Id);

        var status = await _service.DeleteAsync(userId, store.Id);

        Assert.Equal(StoreStatus.Ok, status);
        Assert.Equal([new StoreRow(keep.Id, "open store", groupId)], await _data.GetStoresAsync(groupId));
        var storedPurchase = await _data.FindPurchaseAsync(purchase.Id);
        Assert.NotNull(storedPurchase);
        Assert.Null(storedPurchase.StoreId);
    }

    [Fact]
    public async Task Delete_OtherGroupStore_ReturnsNotFound()
    {
        var (_, userId) = await CreateUserInNewGroupAsync("Group deleting foreign store");
        var otherGroup = await _data.CreateGroupAsync("Victim group with store");
        var foreign = await _data.CreateStoreAsync(otherGroup.Id, "foreign");

        var status = await _service.DeleteAsync(userId, foreign.Id);

        Assert.Equal(StoreStatus.NotFound, status);
        Assert.Single(await _data.GetStoresAsync(otherGroup.Id));
    }
}

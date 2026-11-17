using FamilyBudget.Services;

namespace FamilyBudget.Tests;

// Each test creates its own groups, users, categories and products.
public class ProductServiceTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    private readonly ProductService _service = new(fixture.DbFactory);
    private readonly TestData _data = new(fixture.DbFactory);

    private async Task<(int GroupId, int UserId)> CreateUserInNewGroupAsync(string groupName)
    {
        var group = await _data.CreateGroupAsync(groupName);
        var user = await _data.CreateUserAsync(group.Id);
        return (group.Id, user.Id);
    }

    // ---------- Find or create ----------

    [Fact]
    public async Task FindOrCreate_NewName_CreatesNormalizedProduct()
    {
        var (groupId, userId) = await CreateUserInNewGroupAsync("Group for new product");

        var result = await _service.FindOrCreateAsync(userId, "  Молоко   3,2% ", null);

        Assert.Equal(ProductStatus.Ok, result.Status);
        Assert.True(result.Created);
        Assert.Equal("молоко 3,2%", result.Product!.Name);
        Assert.Null(result.Product.CategoryId);
        var row = Assert.Single(await _data.GetProductsAsync(groupId));
        Assert.Equal(new ProductRow(result.Product.Id, "молоко 3,2%", null, groupId), row);
    }

    [Fact]
    public async Task FindOrCreate_SameNameDifferentCaseAndSpaces_ReturnsExisting_NoNewRow()
    {
        var (groupId, userId) = await CreateUserInNewGroupAsync("Group for existing product");
        var first = await _service.FindOrCreateAsync(userId, "Bread", null);

        var second = await _service.FindOrCreateAsync(userId, "  BREAD\t", null);

        Assert.Equal(ProductStatus.Ok, second.Status);
        Assert.False(second.Created);
        Assert.Equal(first.Product, second.Product);
        Assert.Single(await _data.GetProductsAsync(groupId));
    }

    [Fact]
    public async Task FindOrCreate_SameNameInTwoGroups_CreatesTwoProducts()
    {
        var (firstGroupId, firstUserId) = await CreateUserInNewGroupAsync("First group");
        var (secondGroupId, secondUserId) = await CreateUserInNewGroupAsync("Second group");

        var first = await _service.FindOrCreateAsync(firstUserId, "Eggs", null);
        var second = await _service.FindOrCreateAsync(secondUserId, "Eggs", null);

        Assert.True(first.Created);
        Assert.True(second.Created);
        Assert.NotEqual(first.Product!.Id, second.Product!.Id);
        Assert.Equal(first.Product.Id, Assert.Single(await _data.GetProductsAsync(firstGroupId)).Id);
        Assert.Equal(second.Product.Id, Assert.Single(await _data.GetProductsAsync(secondGroupId)).Id);
    }

    [Fact]
    public async Task FindOrCreate_OwnCategory_SetsCategory()
    {
        var (groupId, userId) = await CreateUserInNewGroupAsync("Group with category");
        var category = await _data.CreateCategoryAsync(groupId, "Dairy");

        var result = await _service.FindOrCreateAsync(userId, "Butter", category.Id);

        Assert.True(result.Created);
        Assert.Equal(category.Id, result.Product!.CategoryId);
        Assert.Equal(category.Id, (await _data.FindProductAsync(result.Product.Id))!.CategoryId);
    }

    [Fact]
    public async Task FindOrCreate_OtherGroupCategory_ReturnsCategoryNotFound_NothingCreated()
    {
        var (groupId, userId) = await CreateUserInNewGroupAsync("Group without products");
        var otherGroup = await _data.CreateGroupAsync("Group with foreign category");
        var foreignCategory = await _data.CreateCategoryAsync(otherGroup.Id, "Foreign");

        var result = await _service.FindOrCreateAsync(userId, "Butter", foreignCategory.Id);

        Assert.Equal(new FindOrCreateProductResult(ProductStatus.CategoryNotFound, null, false), result);
        Assert.Empty(await _data.GetProductsAsync(groupId));
        Assert.Empty(await _data.GetProductsAsync(otherGroup.Id));
    }

    [Fact]
    public async Task FindOrCreate_ExistingProductWithDifferentCategory_CategoryUnchanged()
    {
        var (groupId, userId) = await CreateUserInNewGroupAsync("Group for category check");
        var dairy = await _data.CreateCategoryAsync(groupId, "Dairy");
        var other = await _data.CreateCategoryAsync(groupId, "Other");
        var created = await _service.FindOrCreateAsync(userId, "Cheese", dairy.Id);

        var found = await _service.FindOrCreateAsync(userId, "cheese", other.Id);

        Assert.Equal(ProductStatus.Ok, found.Status);
        Assert.False(found.Created);
        Assert.Equal(dairy.Id, found.Product!.CategoryId);
        Assert.Equal(dairy.Id, (await _data.FindProductAsync(created.Product!.Id))!.CategoryId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\u00A0")]
    public async Task FindOrCreate_EmptyOrWhitespaceName_ReturnsEmptyName(string name)
    {
        var (groupId, userId) = await CreateUserInNewGroupAsync("Group for empty product name");

        var result = await _service.FindOrCreateAsync(userId, name, null);

        Assert.Equal(new FindOrCreateProductResult(ProductStatus.EmptyName, null, false), result);
        Assert.Empty(await _data.GetProductsAsync(groupId));
    }

    // Smoke test for the race handled by the unique index (Architecture Decision #2):
    // whether or not the calls actually collide, the outcome must be one product.
    [Fact]
    public async Task FindOrCreate_ParallelCallsSameName_OneProduct()
    {
        var (groupId, userId) = await CreateUserInNewGroupAsync("Group for parallel calls");

        var results = await Task.WhenAll(
            Enumerable.Range(0, 10).Select(_ => _service.FindOrCreateAsync(userId, "Coffee", null)));

        Assert.All(results, r => Assert.Equal(ProductStatus.Ok, r.Status));
        Assert.Single(results.Select(r => r.Product!.Id).Distinct());
        Assert.Single(results, r => r.Created);
        Assert.Single(await _data.GetProductsAsync(groupId));
    }

    // ---------- Get all ----------

    [Fact]
    public async Task GetAll_ReturnsOnlyOwnGroupProducts_OrderedByName()
    {
        var (groupId, userId) = await CreateUserInNewGroupAsync("Group for get all");
        var cherry = await _data.CreateProductAsync(groupId, "cherry", null);
        var apple = await _data.CreateProductAsync(groupId, "apple", null);
        var category = await _data.CreateCategoryAsync(groupId, "Fruit");
        var banana = await _data.CreateProductAsync(groupId, "banana", category.Id);
        var otherGroup = await _data.CreateGroupAsync("Other group for get all");
        await _data.CreateProductAsync(otherGroup.Id, "avocado", null);

        var products = await _service.GetAllAsync(userId);

        Assert.Equal(
            [
                new ProductInfo(apple.Id, "apple", null),
                new ProductInfo(banana.Id, "banana", category.Id),
                new ProductInfo(cherry.Id, "cherry", null)
            ],
            products);
    }

    // ---------- Rename ----------

    [Fact]
    public async Task Rename_StoresNormalizedName()
    {
        var (groupId, userId) = await CreateUserInNewGroupAsync("Group for product rename");
        var product = await _data.CreateProductAsync(groupId, "old name", null);

        var status = await _service.RenameAsync(userId, product.Id, "  New   NAME ");

        Assert.Equal(ProductStatus.Ok, status);
        Assert.Equal("new name", (await _data.FindProductAsync(product.Id))!.Name);
    }

    [Fact]
    public async Task Rename_ToOtherProductsNameDifferentCase_ReturnsNameTaken_BothUnchanged()
    {
        var (groupId, userId) = await CreateUserInNewGroupAsync("Group for name taken");
        await _data.CreateProductAsync(groupId, "milk", null);
        await _data.CreateProductAsync(groupId, "kefir", null);
        var before = await _data.GetProductsAsync(groupId);
        var kefirId = before.Single(p => p.Name == "kefir").Id;

        var status = await _service.RenameAsync(userId, kefirId, "MILK");

        Assert.Equal(ProductStatus.NameTaken, status);
        Assert.Equal(before, await _data.GetProductsAsync(groupId));
    }

    [Fact]
    public async Task Rename_ToOwnName_ReturnsOk()
    {
        var (groupId, userId) = await CreateUserInNewGroupAsync("Group for own name");
        var product = await _data.CreateProductAsync(groupId, "tea", null);

        var status = await _service.RenameAsync(userId, product.Id, " TEA ");

        Assert.Equal(ProductStatus.Ok, status);
        Assert.Equal("tea", (await _data.FindProductAsync(product.Id))!.Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Rename_EmptyName_ReturnsEmptyName(string name)
    {
        var (groupId, userId) = await CreateUserInNewGroupAsync("Group for empty rename");
        var product = await _data.CreateProductAsync(groupId, "juice", null);

        var status = await _service.RenameAsync(userId, product.Id, name);

        Assert.Equal(ProductStatus.EmptyName, status);
        Assert.Equal("juice", (await _data.FindProductAsync(product.Id))!.Name);
    }

    [Fact]
    public async Task Rename_OtherGroupProduct_ReturnsNotFound()
    {
        var (_, userId) = await CreateUserInNewGroupAsync("Renaming group");
        var otherGroup = await _data.CreateGroupAsync("Group with foreign product");
        var foreign = await _data.CreateProductAsync(otherGroup.Id, "foreign", null);

        var status = await _service.RenameAsync(userId, foreign.Id, "hacked");

        Assert.Equal(ProductStatus.NotFound, status);
        Assert.Equal("foreign", (await _data.FindProductAsync(foreign.Id))!.Name);
    }

    // ---------- Set category ----------

    [Fact]
    public async Task SetCategory_SetThenClear()
    {
        var (groupId, userId) = await CreateUserInNewGroupAsync("Group for set category");
        var category = await _data.CreateCategoryAsync(groupId, "Snacks");
        var product = await _data.CreateProductAsync(groupId, "chips", null);

        var setStatus = await _service.SetCategoryAsync(userId, product.Id, category.Id);
        var afterSet = (await _data.FindProductAsync(product.Id))!.CategoryId;
        var clearStatus = await _service.SetCategoryAsync(userId, product.Id, null);
        var afterClear = (await _data.FindProductAsync(product.Id))!.CategoryId;

        Assert.Equal(ProductStatus.Ok, setStatus);
        Assert.Equal(category.Id, afterSet);
        Assert.Equal(ProductStatus.Ok, clearStatus);
        Assert.Null(afterClear);
    }

    [Fact]
    public async Task SetCategory_OtherGroupCategory_ReturnsCategoryNotFound_Unchanged()
    {
        var (groupId, userId) = await CreateUserInNewGroupAsync("Group for foreign category");
        var own = await _data.CreateCategoryAsync(groupId, "Own");
        var product = await _data.CreateProductAsync(groupId, "rice", own.Id);
        var otherGroup = await _data.CreateGroupAsync("Group owning the category");
        var foreign = await _data.CreateCategoryAsync(otherGroup.Id, "Foreign");

        var status = await _service.SetCategoryAsync(userId, product.Id, foreign.Id);

        Assert.Equal(ProductStatus.CategoryNotFound, status);
        Assert.Equal(own.Id, (await _data.FindProductAsync(product.Id))!.CategoryId);
    }

    [Fact]
    public async Task SetCategory_OtherGroupProduct_ReturnsNotFound()
    {
        var (_, userId) = await CreateUserInNewGroupAsync("Group setting foreign product");
        var otherGroup = await _data.CreateGroupAsync("Group owning the product");
        var foreign = await _data.CreateProductAsync(otherGroup.Id, "foreign", null);

        var status = await _service.SetCategoryAsync(userId, foreign.Id, null);

        Assert.Equal(ProductStatus.NotFound, status);
    }

    // ---------- Delete ----------

    [Fact]
    public async Task Delete_WithoutPurchases_Deleted()
    {
        var (groupId, userId) = await CreateUserInNewGroupAsync("Group for product delete");
        var product = await _data.CreateProductAsync(groupId, "salt", null);
        var keep = await _data.CreateProductAsync(groupId, "pepper", null);

        var status = await _service.DeleteAsync(userId, product.Id);

        Assert.Equal(ProductStatus.Ok, status);
        Assert.Null(await _data.FindProductAsync(product.Id));
        Assert.NotNull(await _data.FindProductAsync(keep.Id));
    }

    [Fact]
    public async Task Delete_WithPurchase_ReturnsInUse_ProductAndPurchaseRemain()
    {
        var (groupId, userId) = await CreateUserInNewGroupAsync("Group for product in use");
        var product = await _data.CreateProductAsync(groupId, "sugar", null);
        var purchase = await _data.CreatePurchaseAsync(groupId, userId, product.Id);

        var status = await _service.DeleteAsync(userId, product.Id);

        Assert.Equal(ProductStatus.InUse, status);
        Assert.NotNull(await _data.FindProductAsync(product.Id));
        Assert.NotNull(await _data.FindPurchaseAsync(purchase.Id));
    }

    [Fact]
    public async Task Delete_OtherGroupProduct_ReturnsNotFound()
    {
        var (_, userId) = await CreateUserInNewGroupAsync("Group deleting foreign product");
        var otherGroup = await _data.CreateGroupAsync("Victim group");
        var foreign = await _data.CreateProductAsync(otherGroup.Id, "foreign", null);

        var status = await _service.DeleteAsync(userId, foreign.Id);

        Assert.Equal(ProductStatus.NotFound, status);
        Assert.NotNull(await _data.FindProductAsync(foreign.Id));
    }
}

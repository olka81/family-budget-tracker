using FamilyBudget.Services;

namespace FamilyBudget.Tests;

// Each test creates its own groups, users and categories (no default tree unless needed),
// so the expected state of every group is fully known inside the test.
public class CategoryServiceTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    private readonly CategoryService _service = new(fixture.DbFactory);
    private readonly TestData _data = new(fixture.DbFactory);

    // ---------- Get tree ----------

    [Fact]
    public async Task GetTree_ReturnsOnlyOwnGroupCategories()
    {
        var group = await _data.CreateGroupAsync("Own group");
        var user = await _data.CreateUserAsync(group.Id);
        var food = await _data.CreateCategoryAsync(group.Id, "Food");
        var dairy = await _data.CreateCategoryAsync(group.Id, "Dairy", food.Id);
        var cheese = await _data.CreateCategoryAsync(group.Id, "Cheese", dairy.Id);
        var rent = await _data.CreateCategoryAsync(group.Id, "Rent");
        var otherGroup = await _data.CreateGroupAsync("Other group");
        await _data.CreateCategoryAsync(otherGroup.Id, "Other group's category");

        var tree = await _service.GetTreeAsync(user.Id);

        Assert.Collection(tree,
            n =>
            {
                Assert.Equal((food.Id, "Food"), (n.Id, n.Name));
                var d = Assert.Single(n.Children);
                Assert.Equal((dairy.Id, "Dairy"), (d.Id, d.Name));
                var c = Assert.Single(d.Children);
                Assert.Equal((cheese.Id, "Cheese"), (c.Id, c.Name));
                Assert.Empty(c.Children);
            },
            n =>
            {
                Assert.Equal((rent.Id, "Rent"), (n.Id, n.Name));
                Assert.Empty(n.Children);
            });
    }

    // ---------- Create ----------

    [Fact]
    public async Task Create_TopLevelAndChild_StoredWithRightGroupAndParent()
    {
        var group = await _data.CreateGroupAsync("Group for create");
        var user = await _data.CreateUserAsync(group.Id);

        var top = await _service.CreateAsync(user.Id, "  Pets  ", null);
        var child = await _service.CreateAsync(user.Id, "Food", top.CategoryId);

        Assert.Equal(CategoryStatus.Ok, top.Status);
        Assert.Equal(CategoryStatus.Ok, child.Status);
        var rows = await _data.GetCategoriesAsync(group.Id);
        Assert.Equal(
            [
                new CategoryRow(top.CategoryId!.Value, "Pets", null, group.Id),
                new CategoryRow(child.CategoryId!.Value, "Food", top.CategoryId, group.Id)
            ],
            rows);
    }

    [Fact]
    public async Task Create_ParentFromOtherGroup_ReturnsNotFound_NothingCreated()
    {
        var group = await _data.CreateGroupAsync("Group without categories");
        var user = await _data.CreateUserAsync(group.Id);
        var otherGroup = await _data.CreateGroupAsync("Foreign group");
        var foreignParent = await _data.CreateCategoryAsync(otherGroup.Id, "Foreign parent");

        var result = await _service.CreateAsync(user.Id, "Sneaky child", foreignParent.Id);

        Assert.Equal(new CreateCategoryResult(CategoryStatus.NotFound, null), result);
        Assert.Empty(await _data.GetCategoriesAsync(group.Id));
        Assert.Single(await _data.GetCategoriesAsync(otherGroup.Id));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Create_EmptyOrWhitespaceName_ReturnsEmptyName(string name)
    {
        var group = await _data.CreateGroupAsync("Group for empty name");
        var user = await _data.CreateUserAsync(group.Id);

        var result = await _service.CreateAsync(user.Id, name, null);

        Assert.Equal(new CreateCategoryResult(CategoryStatus.EmptyName, null), result);
        Assert.Empty(await _data.GetCategoriesAsync(group.Id));
    }

    [Fact]
    public async Task Create_DuplicateSiblingName_IsAllowed()
    {
        var group = await _data.CreateGroupAsync("Group for duplicates");
        var user = await _data.CreateUserAsync(group.Id);
        var parent = await _data.CreateCategoryAsync(group.Id, "Parent");
        await _data.CreateCategoryAsync(group.Id, "Other", parent.Id);

        var result = await _service.CreateAsync(user.Id, "Other", parent.Id);

        Assert.Equal(CategoryStatus.Ok, result.Status);
        var rows = await _data.GetCategoriesAsync(group.Id);
        Assert.Equal(2, rows.Count(r => r.Name == "Other" && r.ParentId == parent.Id));
    }

    // ---------- Rename ----------

    [Fact]
    public async Task Rename_OwnCategory_NameChanged()
    {
        var group = await _data.CreateGroupAsync("Group for rename");
        var user = await _data.CreateUserAsync(group.Id);
        var category = await _data.CreateCategoryAsync(group.Id, "Old name");

        var status = await _service.RenameAsync(user.Id, category.Id, " New name ");

        Assert.Equal(CategoryStatus.Ok, status);
        var row = Assert.Single(await _data.GetCategoriesAsync(group.Id));
        Assert.Equal("New name", row.Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Rename_EmptyOrWhitespaceName_ReturnsEmptyName_NameUnchanged(string name)
    {
        var group = await _data.CreateGroupAsync("Group for empty rename");
        var user = await _data.CreateUserAsync(group.Id);
        var category = await _data.CreateCategoryAsync(group.Id, "Keep me");

        var status = await _service.RenameAsync(user.Id, category.Id, name);

        Assert.Equal(CategoryStatus.EmptyName, status);
        var row = Assert.Single(await _data.GetCategoriesAsync(group.Id));
        Assert.Equal("Keep me", row.Name);
    }

    [Fact]
    public async Task Rename_OtherGroupCategory_ReturnsNotFound_Unchanged()
    {
        var group = await _data.CreateGroupAsync("Renaming group");
        var user = await _data.CreateUserAsync(group.Id);
        var otherGroup = await _data.CreateGroupAsync("Group with foreign category");
        var foreign = await _data.CreateCategoryAsync(otherGroup.Id, "Foreign");

        var status = await _service.RenameAsync(user.Id, foreign.Id, "Hacked");

        Assert.Equal(CategoryStatus.NotFound, status);
        var row = Assert.Single(await _data.GetCategoriesAsync(otherGroup.Id));
        Assert.Equal("Foreign", row.Name);
    }

    // ---------- Delete ----------

    [Fact]
    public async Task Delete_Leaf_OnlyLeafRemoved()
    {
        var group = await _data.CreateGroupAsync("Group for leaf delete");
        var user = await _data.CreateUserAsync(group.Id);
        var parent = await _data.CreateCategoryAsync(group.Id, "Parent");
        var leaf = await _data.CreateCategoryAsync(group.Id, "Leaf", parent.Id);
        var sibling = await _data.CreateCategoryAsync(group.Id, "Sibling", parent.Id);
        var before = await _data.GetCategoriesAsync(group.Id);

        var status = await _service.DeleteAsync(user.Id, leaf.Id);

        Assert.Equal(CategoryStatus.Ok, status);
        Assert.Equal(before.Where(r => r.Id != leaf.Id), await _data.GetCategoriesAsync(group.Id));
    }

    [Fact]
    public async Task Delete_SubtreeThreeLevelsDeep_WholeSubtreeRemoved_RestUnchanged()
    {
        var group = await _data.CreateGroupAsync("Group for subtree delete");
        var user = await _data.CreateUserAsync(group.Id);
        var keepTop = await _data.CreateCategoryAsync(group.Id, "Keep top");
        await _data.CreateCategoryAsync(group.Id, "Keep child", keepTop.Id);
        var root = await _data.CreateCategoryAsync(group.Id, "Root", keepTop.Id);
        var level1a = await _data.CreateCategoryAsync(group.Id, "Level 1a", root.Id);
        var level1b = await _data.CreateCategoryAsync(group.Id, "Level 1b", root.Id);
        var level2 = await _data.CreateCategoryAsync(group.Id, "Level 2", level1a.Id);
        var level3 = await _data.CreateCategoryAsync(group.Id, "Level 3", level2.Id);
        var subtree = new[] { root.Id, level1a.Id, level1b.Id, level2.Id, level3.Id };
        var before = await _data.GetCategoriesAsync(group.Id);

        var status = await _service.DeleteAsync(user.Id, root.Id);

        Assert.Equal(CategoryStatus.Ok, status);
        Assert.Equal(0, await _data.CountCategoriesAsync(subtree));
        Assert.Equal(before.Where(r => !subtree.Contains(r.Id)), await _data.GetCategoriesAsync(group.Id));
    }

    [Fact]
    public async Task Delete_Subtree_ProductsLoseCategory_OtherProductsKeepIt()
    {
        var group = await _data.CreateGroupAsync("Group with products");
        var user = await _data.CreateUserAsync(group.Id);
        var root = await _data.CreateCategoryAsync(group.Id, "Root");
        var child = await _data.CreateCategoryAsync(group.Id, "Child", root.Id);
        var other = await _data.CreateCategoryAsync(group.Id, "Other");
        var inRoot = await _data.CreateProductAsync(group.Id, "Product in root", root.Id);
        var inChild = await _data.CreateProductAsync(group.Id, "Product in child", child.Id);
        var inOther = await _data.CreateProductAsync(group.Id, "Product in other", other.Id);

        var status = await _service.DeleteAsync(user.Id, root.Id);

        Assert.Equal(CategoryStatus.Ok, status);
        Assert.Null((await _data.FindProductAsync(inRoot.Id))!.CategoryId);
        Assert.Null((await _data.FindProductAsync(inChild.Id))!.CategoryId);
        Assert.Equal(other.Id, (await _data.FindProductAsync(inOther.Id))!.CategoryId);
    }

    [Fact]
    public async Task Delete_OtherGroupCategory_ReturnsNotFound_NothingDeleted()
    {
        var group = await _data.CreateGroupAsync("Deleting group");
        var user = await _data.CreateUserAsync(group.Id);
        var own = await _data.CreateCategoryAsync(group.Id, "Own");
        var otherGroup = await _data.CreateGroupAsync("Victim group");
        var foreign = await _data.CreateCategoryAsync(otherGroup.Id, "Foreign");
        await _data.CreateCategoryAsync(otherGroup.Id, "Foreign child", foreign.Id);
        var ownBefore = await _data.GetCategoriesAsync(group.Id);
        var otherBefore = await _data.GetCategoriesAsync(otherGroup.Id);

        var status = await _service.DeleteAsync(user.Id, foreign.Id);

        Assert.Equal(CategoryStatus.NotFound, status);
        Assert.Equal(ownBefore, await _data.GetCategoriesAsync(group.Id));
        Assert.Equal(otherBefore, await _data.GetCategoriesAsync(otherGroup.Id));
    }
}

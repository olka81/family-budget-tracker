using FamilyBudget.Services;

namespace FamilyBudget.Tests;

// Seeding of the default category tree, and how it interacts with leaving and joining groups.
public class DefaultCategoriesTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    private readonly FamilyGroupService _groupService = new(fixture.DbFactory);
    private readonly TestData _data = new(fixture.DbFactory);

    [Fact]
    public async Task Seed_NewGroup_CreatesExactDefaultTree()
    {
        var group = await _data.CreateGroupWithDefaultCategoriesAsync("Seeded group");

        var rows = await _data.GetCategoriesAsync(group.Id);

        ExpectedDefaultTree.AssertMatches(rows);
    }

    [Fact]
    public async Task Leave_Ok_NewPersonalGroupHasDefaultTree_OldGroupCategoriesUnchanged()
    {
        var oldGroup = await _data.CreateGroupAsync("Group being left");
        var leaver = await _data.CreateUserAsync(oldGroup.Id);
        await _data.CreateUserAsync(oldGroup.Id);
        var food = await _data.CreateCategoryAsync(oldGroup.Id, "Food");
        await _data.CreateCategoryAsync(oldGroup.Id, "Fruit", food.Id);
        var oldCategoriesBefore = await _data.GetCategoriesAsync(oldGroup.Id);

        var result = await _groupService.LeaveAsync(leaver.Id);

        Assert.Equal(LeaveStatus.Ok, result.Status);
        var newGroupId = (await _data.FindUserAsync(leaver.Id))!.FamilyGroupId;
        Assert.NotEqual(oldGroup.Id, newGroupId);
        ExpectedDefaultTree.AssertMatches(await _data.GetCategoriesAsync(newGroupId));
        Assert.Equal(oldCategoriesBefore, await _data.GetCategoriesAsync(oldGroup.Id));
    }

    [Fact]
    public async Task Seed_TwoGroups_DoNotShareCategoryRows()
    {
        var first = await _data.CreateGroupWithDefaultCategoriesAsync("First seeded group");
        var second = await _data.CreateGroupWithDefaultCategoriesAsync("Second seeded group");

        var firstRows = await _data.GetCategoriesAsync(first.Id);
        var secondRows = await _data.GetCategoriesAsync(second.Id);

        Assert.Empty(firstRows.Select(r => r.Id).Intersect(secondRows.Select(r => r.Id)));
        AssertParentsInsideGroup(firstRows, first.Id);
        AssertParentsInsideGroup(secondRows, second.Id);
    }

    [Fact]
    public async Task Join_SoleMemberWithNestedDefaultTree_OldGroupAndAllCategoriesDeleted_TargetUnchanged()
    {
        var token = Guid.NewGuid();
        var targetGroup = await _data.CreateGroupWithDefaultCategoriesAsync("Target group");
        await SetInviteTokenAsync(targetGroup.Id, token);
        await _data.CreateUserAsync(targetGroup.Id);
        var targetCategoriesBefore = await _data.GetCategoriesAsync(targetGroup.Id);

        var personalGroup = await _data.CreateGroupWithDefaultCategoriesAsync("Personal group with tree");
        var joiner = await _data.CreateUserAsync(personalGroup.Id);
        var personalCategoryIds = (await _data.GetCategoriesAsync(personalGroup.Id)).Select(r => r.Id).ToList();

        var status = await _groupService.JoinAsync(joiner.Id, token);

        Assert.Equal(JoinStatus.Ok, status);
        Assert.Null(await _data.FindGroupAsync(personalGroup.Id));
        Assert.Equal(0, await _data.CountCategoriesAsync(personalCategoryIds));
        Assert.Equal(targetCategoriesBefore, await _data.GetCategoriesAsync(targetGroup.Id));
    }

    private static void AssertParentsInsideGroup(List<CategoryRow> rows, int groupId)
    {
        var ids = rows.Select(r => r.Id).ToHashSet();
        Assert.All(rows, r =>
        {
            Assert.Equal(groupId, r.GroupId);
            if (r.ParentId is { } parentId)
            {
                Assert.Contains(parentId, ids);
            }
        });
    }

    private async Task SetInviteTokenAsync(int groupId, Guid token)
    {
        await using var db = await fixture.DbFactory.CreateDbContextAsync();
        var group = await db.FamilyGroups.FindAsync(groupId);
        group!.InviteToken = token;
        await db.SaveChangesAsync();
    }
}

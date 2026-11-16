using FamilyBudget.Services;

namespace FamilyBudget.Tests;

// All tests share one PostgreSQL container (class fixture), but each test creates the
// groups and users it needs, so the tests do not depend on each other or on their order.
public class FamilyGroupServiceTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    private readonly FamilyGroupService _service = new(fixture.DbFactory);
    private readonly TestData _data = new(fixture.DbFactory);

    // ---------- Token ----------

    [Fact]
    public async Task GetOrCreateInviteToken_GroupWithoutToken_CreatesAndStoresToken()
    {
        var group = await _data.CreateGroupAsync("Group without token");
        var user = await _data.CreateUserAsync(group.Id);

        var token = await _service.GetOrCreateInviteTokenAsync(user.Id);

        Assert.NotEqual(Guid.Empty, token);
        var storedGroup = await _data.FindGroupAsync(group.Id);
        Assert.Equal(token, storedGroup!.InviteToken);
    }

    [Fact]
    public async Task GetOrCreateInviteToken_CalledTwice_ReturnsSameToken()
    {
        var group = await _data.CreateGroupAsync("Group for two calls");
        var user = await _data.CreateUserAsync(group.Id);

        var first = await _service.GetOrCreateInviteTokenAsync(user.Id);
        var second = await _service.GetOrCreateInviteTokenAsync(user.Id);

        Assert.Equal(first, second);
    }

    [Fact]
    public async Task RegenerateInviteToken_ReturnsDifferentToken_AndOldTokenBecomesInvalid()
    {
        var oldToken = Guid.NewGuid();
        var group = await _data.CreateGroupAsync("Group with regenerated link", oldToken);
        var member = await _data.CreateUserAsync(group.Id);
        // Somebody outside the group who still holds the old link.
        var outsiderGroup = await _data.CreateGroupAsync("Outsider's group");
        var outsider = await _data.CreateUserAsync(outsiderGroup.Id);

        var newToken = await _service.RegenerateInviteTokenAsync(member.Id);

        Assert.NotEqual(oldToken, newToken);
        var preview = await _service.PreviewJoinAsync(outsider.Id, oldToken);
        Assert.Equal(JoinStatus.InvalidToken, preview.Status);
    }

    // ---------- Group info ----------

    [Fact]
    public async Task GetMyGroup_ReturnsGroupNameAndAllMemberEmails()
    {
        var group = await _data.CreateGroupAsync("The Smiths");
        var first = await _data.CreateUserAsync(group.Id);
        var second = await _data.CreateUserAsync(group.Id);

        var info = await _service.GetMyGroupAsync(first.Id);

        Assert.Equal("The Smiths", info.Name);
        // The order of the emails is not part of this test, only the set of members.
        var expected = new[] { first.Email!, second.Email! }.Order(StringComparer.Ordinal);
        Assert.Equal(expected, info.MemberEmails.Order(StringComparer.Ordinal));
    }

    // ---------- Preview ----------

    [Fact]
    public async Task PreviewJoin_UnknownToken_ReturnsInvalidTokenWithNoGroupInfo()
    {
        var group = await _data.CreateGroupAsync("Personal group");
        var user = await _data.CreateUserAsync(group.Id);

        var preview = await _service.PreviewJoinAsync(user.Id, Guid.NewGuid());

        Assert.Equal(JoinStatus.InvalidToken, preview.Status);
        Assert.Null(preview.GroupName);
        Assert.Equal(0, preview.MemberCount);
    }

    [Fact]
    public async Task PreviewJoin_TokenOfOwnGroup_ReturnsAlreadyMember()
    {
        var token = Guid.NewGuid();
        var group = await _data.CreateGroupAsync("Own group", token);
        var user = await _data.CreateUserAsync(group.Id);

        var preview = await _service.PreviewJoinAsync(user.Id, token);

        Assert.Equal(JoinStatus.AlreadyMember, preview.Status);
    }

    [Fact]
    public async Task PreviewJoin_CurrentGroupHasOtherMember_ReturnsHasOtherMembers()
    {
        var currentGroup = await _data.CreateGroupAsync("Current group of two");
        var user = await _data.CreateUserAsync(currentGroup.Id);
        await _data.CreateUserAsync(currentGroup.Id);
        var token = Guid.NewGuid();
        var targetGroup = await _data.CreateGroupAsync("Target group", token);
        await _data.CreateUserAsync(targetGroup.Id);

        var preview = await _service.PreviewJoinAsync(user.Id, token);

        Assert.Equal(JoinStatus.HasOtherMembers, preview.Status);
    }

    [Fact]
    public async Task PreviewJoin_SoleMemberWithValidToken_ReturnsOkWithTargetNameAndMemberCount()
    {
        var personalGroup = await _data.CreateGroupAsync("Personal group");
        var user = await _data.CreateUserAsync(personalGroup.Id);
        var token = Guid.NewGuid();
        var targetGroup = await _data.CreateGroupAsync("The Johnsons", token);
        await _data.CreateUserAsync(targetGroup.Id);
        await _data.CreateUserAsync(targetGroup.Id);

        var preview = await _service.PreviewJoinAsync(user.Id, token);

        Assert.Equal(JoinStatus.Ok, preview.Status);
        Assert.Equal("The Johnsons", preview.GroupName);
        Assert.Equal(2, preview.MemberCount);
    }

    [Fact]
    public async Task PreviewJoin_Ok_ChangesNothingInDatabase()
    {
        var personalToken = Guid.NewGuid();
        var personalGroup = await _data.CreateGroupAsync("Personal group", personalToken);
        var user = await _data.CreateUserAsync(personalGroup.Id);
        var targetToken = Guid.NewGuid();
        var targetGroup = await _data.CreateGroupAsync("Target group", targetToken);
        await _data.CreateUserAsync(targetGroup.Id);

        var preview = await _service.PreviewJoinAsync(user.Id, targetToken);

        // Precondition: this is the case in which a real join WOULD change the data.
        Assert.Equal(JoinStatus.Ok, preview.Status);

        var storedUser = await _data.FindUserAsync(user.Id);
        Assert.Equal(personalGroup.Id, storedUser!.FamilyGroupId);

        var storedPersonalGroup = await _data.FindGroupAsync(personalGroup.Id);
        Assert.NotNull(storedPersonalGroup);
        Assert.Equal("Personal group", storedPersonalGroup.Name);
        Assert.Equal(personalToken, storedPersonalGroup.InviteToken);
        Assert.Equal(1, await _data.CountMembersAsync(personalGroup.Id));

        var storedTargetGroup = await _data.FindGroupAsync(targetGroup.Id);
        Assert.NotNull(storedTargetGroup);
        Assert.Equal("Target group", storedTargetGroup.Name);
        Assert.Equal(targetToken, storedTargetGroup.InviteToken);
        Assert.Equal(1, await _data.CountMembersAsync(targetGroup.Id));
    }

    // ---------- Join ----------

    [Fact]
    public async Task Join_Ok_MovesUserAndDeletesOldPersonalGroup()
    {
        var personalGroup = await _data.CreateGroupAsync("Personal group");
        var user = await _data.CreateUserAsync(personalGroup.Id);
        var token = Guid.NewGuid();
        var targetGroup = await _data.CreateGroupAsync("Target group", token);
        await _data.CreateUserAsync(targetGroup.Id);

        var status = await _service.JoinAsync(user.Id, token);

        Assert.Equal(JoinStatus.Ok, status);
        var storedUser = await _data.FindUserAsync(user.Id);
        Assert.NotNull(storedUser);
        Assert.Equal(targetGroup.Id, storedUser.FamilyGroupId);
        Assert.Null(await _data.FindGroupAsync(personalGroup.Id));
    }

    [Fact]
    public async Task Join_InvalidToken_ReturnsStatusAndChangesNothing()
    {
        var personalGroup = await _data.CreateGroupAsync("Personal group");
        var user = await _data.CreateUserAsync(personalGroup.Id);

        var status = await _service.JoinAsync(user.Id, Guid.NewGuid());

        Assert.Equal(JoinStatus.InvalidToken, status);
        var storedUser = await _data.FindUserAsync(user.Id);
        Assert.Equal(personalGroup.Id, storedUser!.FamilyGroupId);
        Assert.NotNull(await _data.FindGroupAsync(personalGroup.Id));
    }

    [Fact]
    public async Task Join_AlreadyMember_ReturnsStatusAndChangesNothing()
    {
        var token = Guid.NewGuid();
        var group = await _data.CreateGroupAsync("Own group", token);
        var user = await _data.CreateUserAsync(group.Id);

        var status = await _service.JoinAsync(user.Id, token);

        Assert.Equal(JoinStatus.AlreadyMember, status);
        var storedUser = await _data.FindUserAsync(user.Id);
        Assert.Equal(group.Id, storedUser!.FamilyGroupId);
        Assert.NotNull(await _data.FindGroupAsync(group.Id));
    }

    [Fact]
    public async Task Join_HasOtherMembers_ReturnsStatusAndChangesNothing()
    {
        var currentGroup = await _data.CreateGroupAsync("Current group of two");
        var user = await _data.CreateUserAsync(currentGroup.Id);
        var otherMember = await _data.CreateUserAsync(currentGroup.Id);
        var token = Guid.NewGuid();
        var targetGroup = await _data.CreateGroupAsync("Target group", token);
        await _data.CreateUserAsync(targetGroup.Id);

        var status = await _service.JoinAsync(user.Id, token);

        Assert.Equal(JoinStatus.HasOtherMembers, status);
        var storedUser = await _data.FindUserAsync(user.Id);
        Assert.Equal(currentGroup.Id, storedUser!.FamilyGroupId);
        var storedOtherMember = await _data.FindUserAsync(otherMember.Id);
        Assert.Equal(currentGroup.Id, storedOtherMember!.FamilyGroupId);
        Assert.NotNull(await _data.FindGroupAsync(currentGroup.Id));
        Assert.NotNull(await _data.FindGroupAsync(targetGroup.Id));
        Assert.Equal(1, await _data.CountMembersAsync(targetGroup.Id));
    }

    // ---------- Leave ----------

    [Fact]
    public async Task Leave_SoleMember_ReturnsSoleMemberWithNullTokenAndChangesNothing()
    {
        var token = Guid.NewGuid();
        var group = await _data.CreateGroupAsync("Group of one", token);
        var user = await _data.CreateUserAsync(group.Id);

        var result = await _service.LeaveAsync(user.Id);

        Assert.Equal(LeaveStatus.SoleMember, result.Status);
        Assert.Null(result.OldGroupInviteToken);
        var storedUser = await _data.FindUserAsync(user.Id);
        Assert.Equal(group.Id, storedUser!.FamilyGroupId);
        var storedGroup = await _data.FindGroupAsync(group.Id);
        Assert.NotNull(storedGroup);
        Assert.Equal(token, storedGroup.InviteToken);
    }

    [Fact]
    public async Task Leave_GroupWithTwoMembers_MovesUserToNewMyFamilyGroupAndReturnsOldToken()
    {
        var oldToken = Guid.NewGuid();
        var oldGroup = await _data.CreateGroupAsync("Group of two", oldToken);
        var leaver = await _data.CreateUserAsync(oldGroup.Id);
        var otherMember = await _data.CreateUserAsync(oldGroup.Id);

        var result = await _service.LeaveAsync(leaver.Id);

        Assert.Equal(LeaveStatus.Ok, result.Status);
        Assert.Equal(oldToken, result.OldGroupInviteToken);

        // The leaver is now in a new group named "My Family".
        var storedLeaver = await _data.FindUserAsync(leaver.Id);
        Assert.NotEqual(oldGroup.Id, storedLeaver!.FamilyGroupId);
        var newGroup = await _data.FindGroupAsync(storedLeaver.FamilyGroupId);
        Assert.NotNull(newGroup);
        Assert.Equal("My Family", newGroup.Name);

        // The old group is untouched and still has the other member.
        var storedOldGroup = await _data.FindGroupAsync(oldGroup.Id);
        Assert.NotNull(storedOldGroup);
        Assert.Equal(oldToken, storedOldGroup.InviteToken);
        var storedOtherMember = await _data.FindUserAsync(otherMember.Id);
        Assert.Equal(oldGroup.Id, storedOtherMember!.FamilyGroupId);
    }
}

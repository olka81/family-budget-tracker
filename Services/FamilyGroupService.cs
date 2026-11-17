using FamilyBudget.Data;
using FamilyBudget.Models;
using Microsoft.EntityFrameworkCore;

namespace FamilyBudget.Services;

public class FamilyGroupService(IDbContextFactory<FamilyBudgetDbContext> dbFactory)
{
    // Factory, not DbContext itself: each method creates a short-lived context (Architecture Decision #7)
    private readonly IDbContextFactory<FamilyBudgetDbContext> _dbFactory = dbFactory;

    private const string PersonalGroupName = "My Family";

    public async Task<GroupInfo> GetMyGroupAsync(int userId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var groupId = await GetGroupIdAsync(db, userId);

        var name = await db.FamilyGroups
            .Where(g => g.Id == groupId)
            .Select(g => g.Name)
            .SingleAsync();

        var emails = await db.Users
            .Where(u => u.FamilyGroupId == groupId)
            .OrderBy(u => u.Email)
            .Select(u => u.Email ?? "")
            .ToListAsync();

        return new GroupInfo(name, emails);
    }

    public async Task<Guid> GetOrCreateInviteTokenAsync(int userId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var groupId = await GetGroupIdAsync(db, userId);

        var token = await ReadInviteTokenAsync(db, groupId);
        if (token is not null)
        {
            return token.Value;
        }

        // Conditional update: if two requests get here at the same time, only the first one
        // writes its token; both then read back the same stored value.
        Guid? newToken = Guid.NewGuid();
        await db.FamilyGroups
            .Where(g => g.Id == groupId && g.InviteToken == null)
            .ExecuteUpdateAsync(s => s.SetProperty(g => g.InviteToken, newToken));

        token = await ReadInviteTokenAsync(db, groupId);
        return token!.Value;
    }

    public async Task<Guid> RegenerateInviteTokenAsync(int userId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var groupId = await GetGroupIdAsync(db, userId);
        var group = await db.FamilyGroups.SingleAsync(g => g.Id == groupId);

        var newToken = Guid.NewGuid();
        group.InviteToken = newToken;
        await db.SaveChangesAsync();

        return newToken;
    }

    public async Task<JoinPreview> PreviewJoinAsync(int userId, Guid token)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var currentGroupId = await GetGroupIdAsync(db, userId);
        var (preview, _) = await EvaluateJoinAsync(db, currentGroupId, token);

        return preview;
    }

    public async Task<JoinStatus> JoinAsync(int userId, Guid token)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        await using var transaction = await db.Database.BeginTransactionAsync();

        var oldGroupId = await GetGroupIdAsync(db, userId);
        await LockGroupAsync(db, oldGroupId);
        var user = await LoadUserInGroupAsync(db, userId, oldGroupId);

        // All checks are done here, after the lock; an earlier preview is never trusted.
        var (preview, targetGroupId) = await EvaluateJoinAsync(db, oldGroupId, token);
        if (preview.Status != JoinStatus.Ok)
        {
            return preview.Status;
        }

        // Step 1: move the user first. The user's FK to the group is cascading,
        // so deleting the old group before this would delete the user.
        user.FamilyGroupId = targetGroupId!.Value;
        await db.SaveChangesAsync();

        // Step 2: delete the old personal group. Its data is removed by the database cascade.
        // Verified by a test (DefaultCategoriesTests): the cascade works for a group with
        // nested categories despite the Restrict link to the parent category.
        // NOT VERIFIED yet: the Restrict relationships Purchase -> Product and Purchase -> User.
        var oldGroup = await db.FamilyGroups.SingleAsync(g => g.Id == oldGroupId);
        db.FamilyGroups.Remove(oldGroup);
        await db.SaveChangesAsync();

        await transaction.CommitAsync();
        return JoinStatus.Ok;
    }

    public async Task<LeaveResult> LeaveAsync(int userId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        await using var transaction = await db.Database.BeginTransactionAsync();

        var oldGroupId = await GetGroupIdAsync(db, userId);
        await LockGroupAsync(db, oldGroupId);
        var user = await LoadUserInGroupAsync(db, userId, oldGroupId);

        var memberCount = await db.Users.CountAsync(u => u.FamilyGroupId == oldGroupId);
        if (memberCount <= 1)
        {
            return new LeaveResult(LeaveStatus.SoleMember, null);
        }

        var oldToken = await ReadInviteTokenAsync(db, oldGroupId);

        // EF Core inserts the new group with its default categories and then updates
        // the user in one SaveChanges, inside this transaction.
        var newGroup = new FamilyGroup { Name = PersonalGroupName };
        DefaultCategories.AddTo(db, newGroup);
        user.FamilyGroup = newGroup;
        await db.SaveChangesAsync();

        await transaction.CommitAsync();
        return new LeaveResult(LeaveStatus.Ok, oldToken);
    }

    private static Task<int> GetGroupIdAsync(FamilyBudgetDbContext db, int userId) =>
        db.Users
            .Where(u => u.Id == userId)
            .Select(u => u.FamilyGroupId)
            .SingleAsync();

    private static Task<Guid?> ReadInviteTokenAsync(FamilyBudgetDbContext db, int groupId) =>
        db.FamilyGroups
            .Where(g => g.Id == groupId)
            .Select(g => g.InviteToken)
            .SingleAsync();

    // Locks the row of the user's current group until the end of the transaction.
    // Must be called inside a transaction, BEFORE counting the group's members.
    //
    // Race it protects against (JoinAsync): A is the sole member of group GA and joins GB,
    // while at the same moment C joins GA by its link. Without the lock A's transaction
    // still sees one member in GA, moves A and deletes GA - and the cascade deletes C,
    // who has just moved there. With the lock, C's update of its FK to GA has to wait for
    // A's transaction (and then fails on the FK, as an exception), or C commits first and
    // A then counts two members and gets HasOtherMembers.
    //
    // In LeaveAsync the same lock stops the last two members from leaving at the same time,
    // which would leave behind a group with no members.
    private static Task LockGroupAsync(FamilyBudgetDbContext db, int groupId) =>
        db.Database.ExecuteSqlAsync(
            $"""SELECT 1 FROM "FamilyGroups" WHERE "Id" = {groupId} FOR UPDATE""");

    // Loads the tracked user after the group lock is taken and makes sure the user is still
    // in the locked group (a parallel request of the same user could have moved them).
    private static async Task<ApplicationUser> LoadUserInGroupAsync(
        FamilyBudgetDbContext db, int userId, int lockedGroupId)
    {
        var user = await db.Users.SingleAsync(u => u.Id == userId);
        if (user.FamilyGroupId != lockedGroupId)
        {
            throw new InvalidOperationException(
                "The user's group was changed by a concurrent request.");
        }

        return user;
    }

    // Shared read-only checks for PreviewJoinAsync and JoinAsync.
    // Returns the target group's id only when a group with this token exists.
    private static async Task<(JoinPreview Preview, int? TargetGroupId)> EvaluateJoinAsync(
        FamilyBudgetDbContext db, int currentGroupId, Guid token)
    {
        var target = await db.FamilyGroups
            .Where(g => g.InviteToken == token)
            .Select(g => new { g.Id, g.Name })
            .SingleOrDefaultAsync();

        if (target is null)
        {
            return (new JoinPreview(JoinStatus.InvalidToken, null, 0), null);
        }

        var targetMemberCount = await db.Users.CountAsync(u => u.FamilyGroupId == target.Id);

        JoinStatus status;
        if (target.Id == currentGroupId)
        {
            status = JoinStatus.AlreadyMember;
        }
        else if (await db.Users.CountAsync(u => u.FamilyGroupId == currentGroupId) > 1)
        {
            status = JoinStatus.HasOtherMembers;
        }
        else
        {
            status = JoinStatus.Ok;
        }

        return (new JoinPreview(status, target.Name, targetMemberCount), target.Id);
    }
}

using FamilyBudget.Data;
using FamilyBudget.Models;
using Microsoft.EntityFrameworkCore;

namespace FamilyBudget.Tests;

// Helpers that prepare and read back rows directly through the DbContext.
// Every call uses its own short-lived context, so assertions always see what is
// really stored in the database, not something cached by a tracked context.
internal sealed class TestData(IDbContextFactory<FamilyBudgetDbContext> dbFactory)
{
    public async Task<FamilyGroup> CreateGroupAsync(string name, Guid? inviteToken = null)
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        var group = new FamilyGroup { Name = name, InviteToken = inviteToken };
        db.FamilyGroups.Add(group);
        await db.SaveChangesAsync();

        return group;
    }

    // UserManager is not needed: a plain row with a unique email is enough for the service.
    public async Task<ApplicationUser> CreateUserAsync(int groupId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        var email = $"user-{Guid.NewGuid():N}@example.com";
        var user = new ApplicationUser
        {
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            SecurityStamp = Guid.NewGuid().ToString(),
            FamilyGroupId = groupId
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        return user;
    }

    public async Task<FamilyGroup?> FindGroupAsync(int groupId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.FamilyGroups.AsNoTracking().SingleOrDefaultAsync(g => g.Id == groupId);
    }

    public async Task<ApplicationUser?> FindUserAsync(int userId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == userId);
    }

    public async Task<int> CountMembersAsync(int groupId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Users.CountAsync(u => u.FamilyGroupId == groupId);
    }
}

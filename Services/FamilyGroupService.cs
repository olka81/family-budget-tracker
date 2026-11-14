
using FamilyBudget.Data;
using Microsoft.EntityFrameworkCore;

namespace FamilyBudget.Services;

public class FamilyGroupService(IDbContextFactory<FamilyBudgetDbContext> dbFactory)
{
    // Factory, not DbContext itself: each method creates a short-lived context (Architecture Decision #7)
    private readonly IDbContextFactory<FamilyBudgetDbContext> _dbFactory = dbFactory;

    public async Task<GroupInfo> GetMyGroupAsync(int userId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var x = await db.Users.Where(u => u.Id == userId).SingleAsync();   
        return new GroupInfo(...);   // return GroupInfo, NOT Task<GroupInfo> — the compiler wraps it
    }

    public Task<Guid> GetOrCreateInviteTokenAsync(int userId) =>
        throw new NotImplementedException();

    public Task<Guid> RegenerateInviteTokenAsync(int userId)=>
        throw new NotImplementedException();

    public Task<JoinPreview> PreviewJoinAsync(int userId, Guid token)=>
        throw new NotImplementedException();

    public Task<JoinStatus>  JoinAsync(int userId, Guid token)=>
        throw new NotImplementedException();
        
    public Task<LeaveResult> LeaveAsync(int userId)=>
        throw new NotImplementedException();
}
namespace FamilyBudget.Services;

// Join Status: the same enum to check before join, and to join
public enum JoinStatus { Ok, InvalidToken, AlreadyMember, HasOtherMembers }

public record JoinPreview(JoinStatus Status, string? GroupName, int MemberCount);
// if InvalidToken, GroupName = null, MemberCount = 0

public enum LeaveStatus { Ok, SoleMember }

public record LeaveResult(LeaveStatus Status, Guid? OldGroupInviteToken);
// if Ok, Token has a value

public record GroupInfo(string Name, IReadOnlyList<string> MemberEmails);

public record GroupMember(int UserId, string Email);

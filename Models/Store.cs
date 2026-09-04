namespace FamilyBudget.Models;

public class Store
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;

    public int FamilyGroupId { get; set; }
    public FamilyGroup FamilyGroup { get; set; } = null!;
}
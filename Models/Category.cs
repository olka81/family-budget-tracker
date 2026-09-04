namespace FamilyBudget.Models;

public class Category
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;

    public int? ParentCategoryId { get; set; }
    public Category? ParentCategory { get; set; }
    public ICollection<Category> ChildCategories { get; set; } = new List<Category>();

    public int FamilyGroupId { get; set; }
    public FamilyGroup FamilyGroup { get; set; } = null!;
}
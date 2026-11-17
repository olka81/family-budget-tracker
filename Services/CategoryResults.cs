namespace FamilyBudget.Services;

// NotFound: the category (or parent) does not exist or belongs to another group;
// both cases give the same result, so other groups' data is not revealed.
public enum CategoryStatus { Ok, EmptyName, NotFound }

public record CreateCategoryResult(CategoryStatus Status, int? CategoryId);
// if Ok, CategoryId has a value

public record CategoryNode(int Id, string Name, IReadOnlyList<CategoryNode> Children);

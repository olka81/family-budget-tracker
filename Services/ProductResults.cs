namespace FamilyBudget.Services;

// NotFound / CategoryNotFound: the product / category does not exist or belongs to another
// group; both cases give the same result, so other groups' data is not revealed.
public enum ProductStatus { Ok, EmptyName, NotFound, CategoryNotFound, NameTaken, InUse }

public record ProductInfo(int Id, string Name, int? CategoryId);

public record FindOrCreateProductResult(ProductStatus Status, ProductInfo? Product, bool Created);
// if Ok, Product has a value; Created = true only when this call inserted the product

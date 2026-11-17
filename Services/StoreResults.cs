namespace FamilyBudget.Services;

// NotFound: the store does not exist or belongs to another group (same result for both).
public enum StoreStatus { Ok, EmptyName, NotFound }

public record StoreInfo(int Id, string Name);

public record CreateStoreResult(StoreStatus Status, int? StoreId);
// if Ok, StoreId has a value

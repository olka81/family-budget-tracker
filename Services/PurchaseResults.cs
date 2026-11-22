using FamilyBudget.Models;

namespace FamilyBudget.Services;

// Invalid: validation failed, nothing was saved; the details are in Errors / ItemErrors.
// NotFound: the purchase does not exist or belongs to another group (same result for both).
public enum PurchaseStatus { Ok, Invalid, NotFound }

// StoreNotFound / CategoryNotFound: missing or belongs to another group (same result for both).
public enum PurchaseError
{
    NoItems,
    EmptyProductName,
    InvalidPrice,
    InvalidQuantity,
    CategoryNotFound,
    StoreNotFound,
    BuyerNotInGroup,
    InvalidPaymentMethod
}

// Fields shared by all items of one save. BuyerUserId = null means the current user.
public record PurchaseHeader(DateOnly Date, int? StoreId, int? BuyerUserId, PaymentMethod PaymentMethod);

// One line. CategoryId is used only when the product is created by this save.
public record PurchaseItem(string? ProductName, int? CategoryId, decimal Price, decimal Quantity, string? Notes);

// Index = position of the item in the list passed to AddItemsAsync.
// An item with several problems has one ItemError per problem.
public record ItemError(int Index, PurchaseError Error);

public record AddPurchasesResult(
    PurchaseStatus Status,
    IReadOnlyList<int> PurchaseIds,
    DateOnly? SavedDate,
    bool DateCorrected,
    IReadOnlyList<PurchaseError> Errors,
    IReadOnlyList<ItemError> ItemErrors);
// if Ok: PurchaseIds in item order, SavedDate has a value, Errors and ItemErrors are empty
// if Invalid: PurchaseIds empty, SavedDate null; Errors = header problems and NoItems

public record UpdatePurchaseResult(
    PurchaseStatus Status,
    DateOnly? SavedDate,
    bool DateCorrected,
    IReadOnlyList<PurchaseError> Errors);
// if Ok, SavedDate has a value; if Invalid, Errors lists the header and item problems

public record PurchaseInfo(
    int Id,
    DateOnly Date,
    int ProductId,
    string ProductName,
    int? CategoryId,
    string? CategoryName,
    int? StoreId,
    string? StoreName,
    int BuyerUserId,
    string? BuyerEmail,
    PaymentMethod PaymentMethod,
    decimal Price,
    decimal Quantity,
    decimal LineTotal,
    string? Notes);

public record GetPurchaseResult(PurchaseStatus Status, PurchaseInfo? Purchase);
// if Ok, Purchase has a value

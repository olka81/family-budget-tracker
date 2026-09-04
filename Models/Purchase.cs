namespace FamilyBudget.Models;

public class Purchase
{
    public int Id { get; set; }

    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public decimal Price { get; set; }
    public decimal Quantity { get; set; } = 1;
    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Card;
    public DateOnly Date { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow);

    public int? StoreId { get; set; }
    public Store? Store { get; set; }

    public int UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;

    public int FamilyGroupId { get; set; }
    public FamilyGroup FamilyGroup { get; set; } = null!;

    public string Currency { get; set; } = "EUR";
    public string? Notes { get; set; }
}
using FamilyBudget.Data;
using FamilyBudget.Models;

namespace FamilyBudget.Services;

// The single definition of the default category tree that every new family group gets.
// Used at registration and when a user leaves a group (new personal group).
public static class DefaultCategories
{
    private sealed record Node(string Name, params Node[] Children);

    private static readonly Node[] Tree =
    [
        new("Продукты питания",
            new("Молочные",
                new("Кисломолочка"),
                new("Масло"),
                new("Прочее")),
            new("Мясные",
                new("Для готовки"),
                new("Готовые к употреблению")),
            new("Хлебо-булочные"),
            new("Сладкое"),
            new("Яйца"),
            new("Бакалея"),
            new("Овощи и фрукты"),
            new("Напитки"),
            new("Прочее")),
        new("Услуги"),
        new("Аренда"),
        new("Подписки"),
        new("Одежда, обувь"),
        new("Зоотовары",
            new("Корм"),
            new("Наполнитель"),
            new("Прочее"),
            new("Ветеринар")),
        new("Табак и аксессуары"),
        new("Общ.транспорт"),
    ];

    // Adds the default tree for the group to the context as new tracked entities.
    // Nothing is saved here: the caller's SaveChanges inserts the categories, inside the
    // caller's transaction, so the group and its categories are created all-or-nothing.
    // Works for a group that is not saved yet too: everything is linked through navigations.
    public static void AddTo(FamilyBudgetDbContext db, FamilyGroup group)
    {
        foreach (var node in Tree)
        {
            Add(db, group, node, parent: null);
        }
    }

    private static void Add(FamilyBudgetDbContext db, FamilyGroup group, Node node, Category? parent)
    {
        var category = new Category
        {
            Name = node.Name,
            FamilyGroup = group,
            ParentCategory = parent
        };
        db.Categories.Add(category);

        foreach (var child in node.Children)
        {
            Add(db, group, child, category);
        }
    }
}

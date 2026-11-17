using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FamilyBudget.Migrations
{
    /// <inheritdoc />
    public partial class SeedDefaultCategories : Migration
    {
        // Data-only migration: gives every existing family group that has no categories yet
        // the default category tree. Groups created after this migration get the tree from
        // Services/DefaultCategories.cs at creation time.
        // The tree below is a frozen copy of DefaultCategories as of this migration; later
        // changes to DefaultCategories must not be copied here.
        // Rows are inserted in the order of the list, so ordering siblings by Id keeps that order.

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                DECLARE
                    g integer;
                    food integer;
                    sub integer;
                    pets integer;
                BEGIN
                    FOR g IN
                        SELECT fg."Id"
                        FROM "FamilyGroups" fg
                        WHERE NOT EXISTS (
                            SELECT 1 FROM "Categories" c WHERE c."FamilyGroupId" = fg."Id")
                        ORDER BY fg."Id"
                    LOOP
                        INSERT INTO "Categories" ("Name", "ParentCategoryId", "FamilyGroupId")
                        VALUES ('Продукты питания', NULL, g)
                        RETURNING "Id" INTO food;

                        INSERT INTO "Categories" ("Name", "ParentCategoryId", "FamilyGroupId")
                        VALUES ('Молочные', food, g)
                        RETURNING "Id" INTO sub;
                        INSERT INTO "Categories" ("Name", "ParentCategoryId", "FamilyGroupId")
                        VALUES ('Кисломолочка', sub, g),
                               ('Масло', sub, g),
                               ('Прочее', sub, g);

                        INSERT INTO "Categories" ("Name", "ParentCategoryId", "FamilyGroupId")
                        VALUES ('Мясные', food, g)
                        RETURNING "Id" INTO sub;
                        INSERT INTO "Categories" ("Name", "ParentCategoryId", "FamilyGroupId")
                        VALUES ('Для готовки', sub, g),
                               ('Готовые к употреблению', sub, g);

                        INSERT INTO "Categories" ("Name", "ParentCategoryId", "FamilyGroupId")
                        VALUES ('Хлебо-булочные', food, g),
                               ('Сладкое', food, g),
                               ('Яйца', food, g),
                               ('Бакалея', food, g),
                               ('Овощи и фрукты', food, g),
                               ('Напитки', food, g),
                               ('Прочее', food, g);

                        INSERT INTO "Categories" ("Name", "ParentCategoryId", "FamilyGroupId")
                        VALUES ('Услуги', NULL, g),
                               ('Аренда', NULL, g),
                               ('Подписки', NULL, g),
                               ('Одежда, обувь', NULL, g);

                        INSERT INTO "Categories" ("Name", "ParentCategoryId", "FamilyGroupId")
                        VALUES ('Зоотовары', NULL, g)
                        RETURNING "Id" INTO pets;
                        INSERT INTO "Categories" ("Name", "ParentCategoryId", "FamilyGroupId")
                        VALUES ('Корм', pets, g),
                               ('Наполнитель', pets, g),
                               ('Прочее', pets, g),
                               ('Ветеринар', pets, g);

                        INSERT INTO "Categories" ("Name", "ParentCategoryId", "FamilyGroupId")
                        VALUES ('Табак и аксессуары', NULL, g),
                               ('Общ.транспорт', NULL, g);
                    END LOOP;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentionally empty: after this migration users may rename, delete or add
            // categories, so the seeded rows can no longer be told apart from user data.
            // Rolling back must not delete users' categories.
        }
    }
}

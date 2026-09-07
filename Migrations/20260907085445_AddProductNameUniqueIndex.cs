using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FamilyBudget.Migrations
{
    /// <inheritdoc />
    public partial class AddProductNameUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Products_FamilyGroupId",
                table: "Products");

            migrationBuilder.CreateIndex(
                name: "IX_Products_FamilyGroupId_Name",
                table: "Products",
                columns: new[] { "FamilyGroupId", "Name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Products_FamilyGroupId_Name",
                table: "Products");

            migrationBuilder.CreateIndex(
                name: "IX_Products_FamilyGroupId",
                table: "Products",
                column: "FamilyGroupId");
        }
    }
}

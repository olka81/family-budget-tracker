using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FamilyBudget.Migrations
{
    /// <inheritdoc />
    public partial class AddFamilyGroupInviteToken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "InviteToken",
                table: "FamilyGroups",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_FamilyGroups_InviteToken",
                table: "FamilyGroups",
                column: "InviteToken",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_FamilyGroups_InviteToken",
                table: "FamilyGroups");

            migrationBuilder.DropColumn(
                name: "InviteToken",
                table: "FamilyGroups");
        }
    }
}

using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using FamilyBudget.Models;

namespace FamilyBudget.Data;

public class FamilyBudgetDbContext : IdentityDbContext<ApplicationUser, IdentityRole<int>, int>
{
    public FamilyBudgetDbContext(DbContextOptions<FamilyBudgetDbContext> options)
        : base(options)
    {
    }

    public DbSet<FamilyGroup> FamilyGroups => Set<FamilyGroup>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Store> Stores => Set<Store>();
    public DbSet<Purchase> Purchases => Set<Purchase>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Category>()
            .HasOne(c => c.ParentCategory)
            .WithMany(c => c.ChildCategories)
            .HasForeignKey(c => c.ParentCategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Product>()
            .HasOne(p => p.Category)
            .WithMany()
            .HasForeignKey(p => p.CategoryId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<Purchase>()
            .HasOne(p => p.Store)
            .WithMany()
            .HasForeignKey(p => p.StoreId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<Purchase>()
            .HasOne(p => p.Product)
            .WithMany()
            .HasForeignKey(p => p.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Purchase>()
            .HasOne(p => p.User)
            .WithMany()
            .HasForeignKey(p => p.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        // numeric(10,2) / numeric(10,3), as in DB_SCHEMA.
        modelBuilder.Entity<Purchase>()
            .Property(p => p.Price)
            .HasPrecision(10, 2);

        modelBuilder.Entity<Purchase>()
            .Property(p => p.Quantity)
            .HasPrecision(10, 3);

        modelBuilder.Entity<Product>()
            .HasIndex(p => new { p.FamilyGroupId, p.Name })
            .IsUnique();

        modelBuilder.Entity<FamilyGroup>()
            .HasIndex(g => g.InviteToken)
            .IsUnique();
    }
}
using Microsoft.EntityFrameworkCore;
using QuotationApp.API.Models;

namespace QuotationApp.API.Data;

/// <summary>
/// Entity Framework Core DbContext for the Quotation application.
/// </summary>
public class QuotationDbContext : DbContext
{
    public QuotationDbContext(DbContextOptions<QuotationDbContext> options) : base(options)
    {
    }

    public DbSet<ModuleEntity> Modules { get; set; }
    public DbSet<QuotationEntity> Quotations { get; set; }
    public DbSet<QuotationModuleEntity> QuotationModules { get; set; }
    public DbSet<UserEntity> Users { get; set; }
    public DbSet<LoginHistoryEntity> LoginHistory { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ModuleEntity configuration
        modelBuilder.Entity<ModuleEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Pillar).IsRequired().HasMaxLength(100);
            entity.Property(e => e.ModuleName).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Price).HasColumnType("decimal(18,2)");
            entity.HasIndex(e => e.ModuleName).IsUnique();
            entity.ToTable("Modules");
        });

        // UserEntity configuration
        modelBuilder.Entity<UserEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Email).IsRequired().HasMaxLength(200);
            entity.HasIndex(e => e.Email).IsUnique();
            entity.Property(e => e.PasswordHash).IsRequired().HasMaxLength(500);
            entity.Property(e => e.FirstName).HasMaxLength(100);
            entity.Property(e => e.LastName).HasMaxLength(100);
            entity.Property(e => e.IsActive).IsRequired();
            entity.Property(e => e.CreatedAt).IsRequired();
            entity.Property(e => e.LastLoginAt);
            entity.Property(e => e.Role).HasMaxLength(50);
            entity.ToTable("Users");
        });

        // LoginHistoryEntity configuration
        modelBuilder.Entity<LoginHistoryEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Email).IsRequired().HasMaxLength(200);
            entity.Property(e => e.LoggedAt).IsRequired();
            entity.ToTable("LoginHistory");
        });

        // QuotationEntity configuration
        modelBuilder.Entity<QuotationEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasMaxLength(50).ValueGeneratedNever();
            entity.Property(e => e.OrganizationName).IsRequired().HasMaxLength(200);
            entity.Property(e => e.ValidationDate).IsRequired();
            entity.Property(e => e.QuotationNo).HasMaxLength(50);
            entity.Property(e => e.Date);
            entity.Property(e => e.QuotationToName).IsRequired().HasMaxLength(150);
            entity.Property(e => e.QuotationToAddress).IsRequired().HasMaxLength(400);
            entity.Property(e => e.QuotationToContactNo).IsRequired().HasMaxLength(30);
            entity.Property(e => e.QuotationToEmail).IsRequired().HasMaxLength(150);
            entity.Property(e => e.GeneratedAt).IsRequired();
            entity.ToTable("Quotations");
        });

        // QuotationModuleEntity configuration (junction table)
        modelBuilder.Entity<QuotationModuleEntity>(entity =>
        {
            entity.HasKey(e => new { e.QuotationId, e.ModuleName });
            entity.Property(e => e.QuotationId).HasMaxLength(50);
            entity.Property(e => e.ModuleName).HasMaxLength(200);
            entity.ToTable("QuotationModules");

            entity.HasOne<QuotationEntity>()
                .WithMany(q => q.QuotationModules)
                .HasForeignKey(e => e.QuotationId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne<ModuleEntity>()
                .WithMany()
                .HasForeignKey(e => e.ModuleName)
                .HasPrincipalKey(m => m.ModuleName)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}

/// <summary>
/// Database entity for Module master data.
/// </summary>
public class ModuleEntity
{
    public int Id { get; set; }
    public string Pillar { get; set; } = string.Empty;
    public string ModuleName { get; set; } = string.Empty;
    public decimal? Price { get; set; }
}

/// <summary>
/// Database entity for Quotation header data.
/// </summary>
public class QuotationEntity
{
    public string Id { get; set; } = string.Empty;
    public string OrganizationName { get; set; } = string.Empty;
    public DateTime ValidationDate { get; set; }
    public string? QuotationNo { get; set; }
    public DateTime? Date { get; set; }
    public string QuotationToName { get; set; } = string.Empty;
    public string QuotationToAddress { get; set; } = string.Empty;
    public string QuotationToContactNo { get; set; } = string.Empty;
    public string QuotationToEmail { get; set; } = string.Empty;
    public DateTime GeneratedAt { get; set; }

    public List<QuotationModuleEntity> QuotationModules { get; set; } = new();
}

/// <summary>
/// Junction entity for Quotation-Module many-to-many relationship.
/// </summary>
public class QuotationModuleEntity
{
    public string QuotationId { get; set; } = string.Empty;
    public string ModuleName { get; set; } = string.Empty;
}

/// <summary>
/// Database entity for application users.
/// </summary>
public class UserEntity
{
    public int Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAt { get; set; }
    public string Role { get; set; } = "User";
}

/// <summary>
/// Tracks login attempts for auditing.
/// </summary>
public class LoginHistoryEntity
{
    public int Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public DateTime LoggedAt { get; set; }
    public string? RemoteAddress { get; set; }
}

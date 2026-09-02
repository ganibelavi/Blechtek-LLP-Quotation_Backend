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
    public DbSet<QuotationHistoryEntity> QuotationHistory { get; set; }
    public DbSet<CustomerEntity> Customers { get; set; }
    public DbSet<SupplierEntity> Suppliers { get; set; }
    public DbSet<ProductEntity> Products { get; set; }
    public DbSet<PurchaseOrderEntity> PurchaseOrders { get; set; }
    public DbSet<PurchaseOrderItemEntity> PurchaseOrderItems { get; set; }
    public DbSet<InvoiceEntity> Invoices { get; set; }
    public DbSet<InvoiceItemEntity> InvoiceItems { get; set; }

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

        modelBuilder.Entity<QuotationHistoryEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.OrganizationName).IsRequired().HasMaxLength(200);
            entity.Property(e => e.QuotationId).IsRequired().HasMaxLength(50);
            entity.Property(e => e.QuotationNo).HasMaxLength(50);
            entity.Property(e => e.ReferenceBy).HasMaxLength(150);
            entity.Property(e => e.QuotationToName).IsRequired().HasMaxLength(150);
            entity.Property(e => e.QuotationToAddress).IsRequired().HasMaxLength(400);
            entity.Property(e => e.QuotationToContactNo).IsRequired().HasMaxLength(30);
            entity.Property(e => e.QuotationToEmail).IsRequired().HasMaxLength(150);
            entity.Property(e => e.ModulesJson).IsRequired();
            entity.Property(e => e.DiscountPercentage).HasColumnType("decimal(5,2)");
            entity.Property(e => e.ChangedAt).IsRequired();
            entity.Property(e => e.ChangeType).IsRequired().HasMaxLength(30);
            entity.HasIndex(e => new { e.OrganizationName, e.ModulesJson });
            entity.HasIndex(e => e.QuotationId);
            entity.ToTable("QuotationHistory");
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
            entity.Property(e => e.ReferenceBy).HasMaxLength(150);
            entity.Property(e => e.CreatedByUser).HasMaxLength(200);
            entity.Property(e => e.QuotationToName).IsRequired().HasMaxLength(150);
            entity.Property(e => e.QuotationToAddress).IsRequired().HasMaxLength(400);
            entity.Property(e => e.QuotationToContactNo).IsRequired().HasMaxLength(30);
            entity.Property(e => e.QuotationToEmail).IsRequired().HasMaxLength(150);
            entity.Property(e => e.GeneratedAt).IsRequired();
            entity.Property(e => e.DiscountPercentage).HasColumnType("decimal(5,2)");
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

        modelBuilder.Entity<CustomerEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Name).IsRequired().HasMaxLength(255).HasColumnName("name");
            entity.Property(e => e.Address).HasMaxLength(1000).HasColumnName("address");
            entity.Property(e => e.State).HasMaxLength(100).HasColumnName("state");
            entity.Property(e => e.StateCode).HasMaxLength(10).HasColumnName("state_code");
            entity.Property(e => e.Gstn).HasMaxLength(20).HasColumnName("gstn");
            entity.Property(e => e.CreatedAt).IsRequired().HasColumnName("created_at");
            entity.ToTable("customers");
        });

        modelBuilder.Entity<SupplierEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Name).IsRequired().HasMaxLength(255).HasColumnName("name");
            entity.Property(e => e.Address).HasMaxLength(1000).HasColumnName("address");
            entity.Property(e => e.State).HasMaxLength(100).HasColumnName("state");
            entity.Property(e => e.StateCode).HasMaxLength(10).HasColumnName("state_code");
            entity.Property(e => e.Gstn).HasMaxLength(20).HasColumnName("gstn");
            entity.Property(e => e.CreatedAt).IsRequired().HasColumnName("created_at");
            entity.ToTable("suppliers");
        });

        modelBuilder.Entity<ProductEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Name).IsRequired().HasMaxLength(255).HasColumnName("name");
            entity.Property(e => e.HsnSac).HasMaxLength(20).HasColumnName("hsn_sac");
            entity.Property(e => e.Uom).HasMaxLength(20).HasColumnName("uom");
            entity.Property(e => e.DefaultRate).HasColumnType("decimal(12,2)").HasColumnName("default_rate");
            entity.Property(e => e.CreatedAt).IsRequired().HasColumnName("created_at");
            entity.ToTable("products");
        });

        modelBuilder.Entity<PurchaseOrderEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CustomerId).IsRequired().HasColumnName("customer_id");
            entity.Property(e => e.SupplierId).HasColumnName("supplier_id");
            entity.Property(e => e.QuotationId).HasColumnName("quotation_id");
            entity.Property(e => e.QuotationRefNo).HasMaxLength(100).HasColumnName("quotation_ref_no");
            entity.Property(e => e.QuotationRefDate).HasColumnName("quotation_ref_date");
            entity.Property(e => e.PoNo).IsRequired().HasMaxLength(50).HasColumnName("po_no");
            entity.Property(e => e.PoDate).IsRequired().HasColumnName("po_date");
            entity.Property(e => e.Status).IsRequired().HasMaxLength(20).HasColumnName("status");
            entity.Property(e => e.DeliveryTerms).HasColumnName("delivery_terms");
            entity.Property(e => e.PaymentTerms).HasColumnName("payment_terms");
            entity.Property(e => e.CreatedAt).IsRequired().HasColumnName("created_at");
            entity.HasIndex(e => e.PoNo).IsUnique();
            entity.ToTable("purchase_orders");

            entity.HasOne<CustomerEntity>()
                .WithMany(c => c.PurchaseOrders)
                .HasForeignKey(e => e.CustomerId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne<SupplierEntity>()
                .WithMany(s => s.PurchaseOrders)
                .HasForeignKey(e => e.SupplierId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PurchaseOrderItemEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.PoId).IsRequired().HasColumnName("po_id");
            entity.Property(e => e.ProductId).HasColumnName("product_id");
            entity.Property(e => e.Description).IsRequired().HasColumnName("description");
            entity.Property(e => e.Qty).HasColumnType("decimal(12,2)").HasColumnName("qty");
            entity.Property(e => e.Uom).HasMaxLength(20).HasColumnName("uom");
            entity.Property(e => e.Rate).HasColumnType("decimal(12,2)").HasColumnName("rate");
            entity.ToTable("po_items");

            entity.HasOne(e => e.PurchaseOrder)
                .WithMany(p => p.Items)
                .HasForeignKey(e => e.PoId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<InvoiceEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CustomerId).IsRequired().HasColumnName("customer_id");
            entity.Property(e => e.PoId).HasColumnName("po_id");
            entity.Property(e => e.InvoiceNo).IsRequired().HasMaxLength(50).HasColumnName("invoice_no");
            entity.Property(e => e.InvoiceDate).IsRequired().HasColumnName("invoice_date");
            entity.Property(e => e.PlaceOfSupply).HasMaxLength(100).HasColumnName("place_of_supply");
            entity.Property(e => e.HsnCode).HasMaxLength(20).HasColumnName("hsn_code");
            entity.Property(e => e.SacCode).HasMaxLength(20).HasColumnName("sac_code");
            entity.Property(e => e.SgstPct).HasColumnType("decimal(5,2)").HasColumnName("sgst_pct");
            entity.Property(e => e.CgstPct).HasColumnType("decimal(5,2)").HasColumnName("cgst_pct");
            entity.Property(e => e.IgstPct).HasColumnType("decimal(5,2)").HasColumnName("igst_pct");
            entity.Property(e => e.TdsPct).HasColumnType("decimal(5,2)").HasColumnName("tds_pct");
            entity.Property(e => e.Insurance).HasColumnType("decimal(12,2)").HasColumnName("insurance");
            entity.Property(e => e.ReverseCharge).HasColumnName("reverse_charge");
            entity.Property(e => e.Subtotal).HasColumnType("decimal(14,2)").HasColumnName("subtotal");
            entity.Property(e => e.GrandTotal).HasColumnType("decimal(14,2)").HasColumnName("grand_total");
            entity.Property(e => e.Status).IsRequired().HasMaxLength(20).HasColumnName("status");
            entity.Property(e => e.AmountInWords).HasMaxLength(1000).HasColumnName("amount_in_words");
            entity.Property(e => e.CreatedAt).IsRequired().HasColumnName("created_at");
            entity.HasIndex(e => e.InvoiceNo).IsUnique();
            entity.ToTable("invoices");

            entity.HasOne<CustomerEntity>()
                .WithMany(c => c.Invoices)
                .HasForeignKey(e => e.CustomerId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<InvoiceItemEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.InvoiceId).IsRequired().HasColumnName("invoice_id");
            entity.Property(e => e.ProductId).HasColumnName("product_id");
            entity.Property(e => e.Description).IsRequired().HasColumnName("description");
            entity.Property(e => e.Qty).HasColumnType("decimal(12,2)").HasColumnName("qty");
            entity.Property(e => e.Uom).HasMaxLength(20).HasColumnName("uom");
            entity.Property(e => e.Rate).HasColumnType("decimal(12,2)").HasColumnName("rate");
            entity.ToTable("invoice_items");

            entity.HasOne(e => e.Invoice)
                .WithMany(i => i.Items)
                .HasForeignKey(e => e.InvoiceId)
                .OnDelete(DeleteBehavior.Cascade);
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
    public string? ReferenceBy { get; set; }
    public string? CreatedByUser { get; set; }
    public string QuotationToName { get; set; } = string.Empty;
    public string QuotationToAddress { get; set; } = string.Empty;
    public string QuotationToContactNo { get; set; } = string.Empty;
    public string QuotationToEmail { get; set; } = string.Empty;
    public DateTime GeneratedAt { get; set; }
    public decimal? DiscountPercentage { get; set; }

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

/// <summary>
/// Immutable snapshot of a quotation used to display revision history.
/// </summary>
public class QuotationHistoryEntity
{
    public int Id { get; set; }
    public string QuotationId { get; set; } = string.Empty;
    public string OrganizationName { get; set; } = string.Empty;
    public string? QuotationNo { get; set; }
    public DateTime? Date { get; set; }
    public DateTime ValidationDate { get; set; }
    public string? ReferenceBy { get; set; }
    public string QuotationToName { get; set; } = string.Empty;
    public string QuotationToAddress { get; set; } = string.Empty;
    public string QuotationToContactNo { get; set; } = string.Empty;
    public string QuotationToEmail { get; set; } = string.Empty;
    public string ModulesJson { get; set; } = "[]";
    public decimal? DiscountPercentage { get; set; }
    public DateTime ChangedAt { get; set; }
    public string ChangeType { get; set; } = string.Empty;
}

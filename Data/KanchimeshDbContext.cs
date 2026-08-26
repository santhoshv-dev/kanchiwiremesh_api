using KanchimeshAPI.Models;
using Microsoft.EntityFrameworkCore;

namespace KanchimeshAPI.Data;

public sealed class KanchimeshDbContext(DbContextOptions<KanchimeshDbContext> options) : DbContext(options)
{
    public DbSet<ApplicationUser> ApplicationUsers => Set<ApplicationUser>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    public DbSet<Enquiry> Enquiries => Set<Enquiry>();
    public DbSet<EmailDeliveryJob> EmailDeliveryJobs => Set<EmailDeliveryJob>();
    public DbSet<ApplicationNotification> Notifications => Set<ApplicationNotification>();
    public DbSet<SalesOrder> SalesOrders => Set<SalesOrder>();
    public DbSet<SalesOrderItem> SalesOrderItems => Set<SalesOrderItem>();
    public DbSet<Payment> Payments => Set<Payment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        ConfigureAuditEntity<ApplicationUser>(modelBuilder.Entity<ApplicationUser>());
        ConfigureAuditEntity<Customer>(modelBuilder.Entity<Customer>());
        ConfigureAuditEntity<Product>(modelBuilder.Entity<Product>());
        ConfigureAuditEntity<StockMovement>(modelBuilder.Entity<StockMovement>());
        ConfigureAuditEntity<Enquiry>(modelBuilder.Entity<Enquiry>());
        ConfigureAuditEntity<EmailDeliveryJob>(modelBuilder.Entity<EmailDeliveryJob>());
        ConfigureAuditEntity<ApplicationNotification>(modelBuilder.Entity<ApplicationNotification>());
        ConfigureAuditEntity<SalesOrder>(modelBuilder.Entity<SalesOrder>());
        ConfigureAuditEntity<Payment>(modelBuilder.Entity<Payment>());

        modelBuilder.Entity<ApplicationUser>(entity =>
        {
            entity.HasIndex(x => x.NormalizedEmail).IsUnique();
            entity.Property(x => x.Email).HasMaxLength(254).IsRequired();
            entity.Property(x => x.NormalizedEmail).HasMaxLength(254).IsRequired();
            entity.Property(x => x.DisplayName).HasMaxLength(150).IsRequired();
            entity.Property(x => x.Role).HasMaxLength(50).IsRequired();
            entity.Property(x => x.PasswordHash).HasMaxLength(1024).IsRequired();
            entity.Property(x => x.LastLoginAtUtc).HasColumnType("datetime2");
            entity.Property(x => x.MustChangePassword).HasDefaultValue(false);
            entity.Property(x => x.IsActive).HasDefaultValue(true);
        });

        modelBuilder.Entity<Customer>(entity =>
        {
            entity.HasIndex(x => x.CustomerCode).IsUnique();
            entity.Property(x => x.CustomerCode).HasMaxLength(48).IsRequired();
            entity.Property(x => x.ContactName).HasMaxLength(150).IsRequired();
            entity.Property(x => x.CompanyName).HasMaxLength(180);
            entity.Property(x => x.Phone).HasMaxLength(25).IsRequired();
            entity.Property(x => x.AlternatePhone).HasMaxLength(25);
            entity.Property(x => x.WhatsAppNumber).HasMaxLength(25);
            entity.Property(x => x.Email).HasMaxLength(254);
            entity.Property(x => x.Address).HasMaxLength(500);
            entity.Property(x => x.City).HasMaxLength(100);
            entity.Property(x => x.District).HasMaxLength(100);
            entity.Property(x => x.State).HasMaxLength(100);
            entity.Property(x => x.PostalCode).HasMaxLength(15);
            entity.Property(x => x.GstNumber).HasMaxLength(32);
            entity.Property(x => x.BusinessType).HasMaxLength(100);
            entity.Property(x => x.Notes).HasMaxLength(2000);
        });

        modelBuilder.Entity<Product>(entity =>
        {
            entity.HasIndex(x => x.ProductCode).IsUnique();
            entity.Property(x => x.ProductCode).HasMaxLength(48).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(180).IsRequired();
            entity.Property(x => x.Category).HasMaxLength(100).IsRequired();
            entity.Property(x => x.MeshType).HasMaxLength(100);
            entity.Property(x => x.MeshOpening).HasMaxLength(100);
            entity.Property(x => x.WireDiameter).HasMaxLength(100);
            entity.Property(x => x.Unit).HasMaxLength(30).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(2000);
            entity.Property(x => x.Rate).HasPrecision(18, 2);
            entity.Property(x => x.IgstRate).HasPrecision(5, 2); entity.Property(x => x.SgstRate).HasPrecision(5, 2); entity.Property(x => x.CgstRate).HasPrecision(5, 2);
            entity.Property(x => x.Width).HasPrecision(18, 3);
            entity.Property(x => x.Length).HasPrecision(18, 3);
            entity.Property(x => x.QuantityOnHand).HasPrecision(18, 3).HasDefaultValue(0m);
            entity.Property(x => x.TotalStockAdded).HasPrecision(18, 3).HasDefaultValue(0m);
            entity.Property(x => x.TotalSold).HasPrecision(18, 3).HasDefaultValue(0m);
            entity.Property(x => x.ReorderLevel).HasPrecision(18, 3).HasDefaultValue(0m);
        });

        modelBuilder.Entity<StockMovement>(entity =>
        {
            entity.HasIndex(x => new { x.ProductId, x.OccurredAtUtc, x.Id });
            entity.HasIndex(x => new { x.MovementType, x.OccurredAtUtc });
            entity.Property(x => x.QuantityChange).HasPrecision(18, 3);
            entity.Property(x => x.BalanceAfter).HasPrecision(18, 3);
            entity.Property(x => x.MovementType).HasMaxLength(30).IsRequired();
            entity.Property(x => x.Reason).HasMaxLength(500);
            entity.Property(x => x.Reference).HasMaxLength(150);
            entity.Property(x => x.OccurredAtUtc).HasColumnType("datetime2");
            entity.HasOne(x => x.Product)
                .WithMany(x => x.StockMovements)
                .HasForeignKey(x => x.ProductId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Enquiry>(entity =>
        {
            entity.HasIndex(x => x.EnquiryNumber).IsUnique();
            entity.HasIndex(x => x.PublicSubmissionKey)
                .IsUnique()
                .HasFilter("[PublicSubmissionKey] IS NOT NULL");
            entity.Property(x => x.EnquiryNumber).HasMaxLength(48).IsRequired();
            entity.Property(x => x.PublicSubmissionKey).HasMaxLength(128);
            entity.Property(x => x.ContactName).HasMaxLength(150).IsRequired();
            entity.Property(x => x.CompanyName).HasMaxLength(180);
            entity.Property(x => x.Phone).HasMaxLength(25).IsRequired();
            entity.Property(x => x.Email).HasMaxLength(254);
            entity.Property(x => x.ProductRequirement).HasMaxLength(300);
            entity.Property(x => x.Unit).HasMaxLength(30);
            entity.Property(x => x.Message).HasMaxLength(4000);
            entity.Property(x => x.Note).HasMaxLength(2000);
            entity.Property(x => x.Status).HasMaxLength(30).IsRequired();
            entity.Property(x => x.Quantity).HasPrecision(18, 3);
            entity.Property(x => x.EmailDeliveryStatus)
                .HasMaxLength(30)
                .HasDefaultValue(EmailDeliveryStatuses.NotRequested)
                .IsRequired();
            entity.Property(x => x.EmailDeliveryAttemptedAtUtc).HasColumnType("datetime2");
            entity.HasOne(x => x.Customer)
                .WithMany(x => x.Enquiries)
                .HasForeignKey(x => x.CustomerId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<EmailDeliveryJob>(entity =>
        {
            entity.HasIndex(x => new { x.Status, x.NextAttemptAtUtc });
            entity.HasIndex(x => new { x.EnquiryId, x.Kind, x.Recipient }).IsUnique();
            entity.Property(x => x.Kind).HasMaxLength(50).IsRequired();
            entity.Property(x => x.Recipient).HasMaxLength(254).IsRequired();
            entity.Property(x => x.Status)
                .HasMaxLength(30)
                .HasDefaultValue(EmailDeliveryJobStatuses.Pending)
                .IsRequired();
            entity.Property(x => x.AttemptCount).HasDefaultValue(0);
            entity.Property(x => x.NextAttemptAtUtc).HasColumnType("datetime2");
            entity.Property(x => x.LockedUntilUtc).HasColumnType("datetime2");
            entity.Property(x => x.LastAttemptAtUtc).HasColumnType("datetime2");
            entity.Property(x => x.SentAtUtc).HasColumnType("datetime2");
            entity.Property(x => x.LastError).HasMaxLength(2000);
            entity.HasOne(x => x.Enquiry)
                .WithMany(x => x.EmailDeliveryJobs)
                .HasForeignKey(x => x.EnquiryId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ApplicationNotification>(entity =>
        {
            entity.HasIndex(x => new { x.IsRead, x.CreatedAtUtc });
            entity.HasIndex(x => x.RelatedEnquiryId);
            entity.HasIndex(x => x.RelatedCustomerId);
            entity.Property(x => x.Title).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Message).HasMaxLength(2000).IsRequired();
            entity.Property(x => x.Type).HasMaxLength(50).IsRequired();
            entity.Property(x => x.ReadAtUtc).HasColumnType("datetime2");
        });

        modelBuilder.Entity<SalesOrder>(entity =>
        {
            entity.HasIndex(x => x.OrderNumber).IsUnique();
            entity.Property(x => x.OrderNumber).HasMaxLength(64).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(30).IsRequired();
            entity.Property(x => x.Notes).HasMaxLength(2000);
            entity.Property(x => x.GstType).HasMaxLength(30);
            entity.Property(x => x.Subtotal).HasPrecision(18, 3);
            entity.Property(x => x.DiscountAmount).HasPrecision(18, 2);
            entity.Property(x => x.FreightAmount).HasPrecision(18, 2);
            entity.Property(x => x.TaxAmount).HasPrecision(18, 2);
            entity.Property(x => x.GrandTotal).HasPrecision(18, 2);
            entity.HasOne(x => x.Customer)
                .WithMany(x => x.Orders)
                .HasForeignKey(x => x.CustomerId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<SalesOrderItem>(entity =>
        {
            entity.Property(x => x.Description).HasMaxLength(300).IsRequired();
            entity.Property(x => x.Specification).HasMaxLength(1000);
            entity.Property(x => x.Unit).HasMaxLength(30).IsRequired();
            entity.Property(x => x.Quantity).HasPrecision(18, 3);
            entity.Property(x => x.Rate).HasPrecision(18, 2);
            entity.Property(x => x.IgstRate).HasPrecision(5, 2); entity.Property(x => x.SgstRate).HasPrecision(5, 2); entity.Property(x => x.CgstRate).HasPrecision(5, 2);
            entity.Property(x => x.LineSubtotal).HasPrecision(18, 2);
            entity.Property(x => x.TaxAmount).HasPrecision(18, 2);
            entity.Property(x => x.LineTotal).HasPrecision(18, 2);
            entity.HasOne(x => x.SalesOrder)
                .WithMany(x => x.Items)
                .HasForeignKey(x => x.SalesOrderId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.Product)
                .WithMany(x => x.OrderItems)
                .HasForeignKey(x => x.ProductId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Payment>(entity =>
        {
            entity.HasIndex(x => x.PaymentNumber).IsUnique();
            entity.Property(x => x.PaymentNumber).HasMaxLength(48).IsRequired();
            entity.Property(x => x.Method).HasMaxLength(30).IsRequired();
            entity.Property(x => x.Reference).HasMaxLength(150);
            entity.Property(x => x.Notes).HasMaxLength(2000);
            entity.Property(x => x.Amount).HasPrecision(18, 2);
            entity.HasOne(x => x.Customer)
                .WithMany(x => x.Payments)
                .HasForeignKey(x => x.CustomerId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.SalesOrder)
                .WithMany(x => x.Payments)
                .HasForeignKey(x => x.SalesOrderId)
                .OnDelete(DeleteBehavior.SetNull);
        });
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        StampAuditFields();
        return base.SaveChangesAsync(cancellationToken);
    }

    public override int SaveChanges()
    {
        StampAuditFields();
        return base.SaveChanges();
    }

    private void StampAuditFields()
    {
        var now = DateTime.UtcNow;
        foreach (var entry in ChangeTracker.Entries<AuditableEntity>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAtUtc = now;
                entry.Entity.UpdatedAtUtc = now;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAtUtc = now;
            }
        }
    }

    private static void ConfigureAuditEntity<TEntity>(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<TEntity> entity)
        where TEntity : AuditableEntity
    {
        entity.Property(x => x.CreatedAtUtc).HasColumnType("datetime2");
        entity.Property(x => x.UpdatedAtUtc).HasColumnType("datetime2");
        entity.Property(x => x.RowVersion).IsRowVersion();
    }
}

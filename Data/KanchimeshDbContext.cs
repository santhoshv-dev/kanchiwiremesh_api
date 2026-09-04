using KanchimeshAPI.Models;
using Microsoft.EntityFrameworkCore;

namespace KanchimeshAPI.Data;

public sealed class KanchimeshDbContext(DbContextOptions<KanchimeshDbContext> options) : DbContext(options)
{
    public DbSet<ApplicationUser> ApplicationUsers => Set<ApplicationUser>();
    public DbSet<CompanyProfile> CompanyProfiles => Set<CompanyProfile>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<RawMaterial> RawMaterials => Set<RawMaterial>();
    public DbSet<ProductRawMaterial> ProductRawMaterials => Set<ProductRawMaterial>();

    public DbSet<Enquiry> Enquiries => Set<Enquiry>();
    public DbSet<EmailDeliveryJob> EmailDeliveryJobs => Set<EmailDeliveryJob>();
    public DbSet<ApplicationNotification> Notifications => Set<ApplicationNotification>();
    public DbSet<SalesOrder> SalesOrders => Set<SalesOrder>();
    public DbSet<SalesOrderItem> SalesOrderItems => Set<SalesOrderItem>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<ProductTransaction> ProductTransactions => Set<ProductTransaction>();
    public DbSet<PurchaseRecord> PurchaseRecords => Set<PurchaseRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        ConfigureAuditEntity<ApplicationUser>(modelBuilder.Entity<ApplicationUser>());
        ConfigureAuditEntity<CompanyProfile>(modelBuilder.Entity<CompanyProfile>());
        ConfigureAuditEntity<Customer>(modelBuilder.Entity<Customer>());
        ConfigureAuditEntity<Product>(modelBuilder.Entity<Product>());
        ConfigureAuditEntity<RawMaterial>(modelBuilder.Entity<RawMaterial>());

        ConfigureAuditEntity<Enquiry>(modelBuilder.Entity<Enquiry>());
        ConfigureAuditEntity<EmailDeliveryJob>(modelBuilder.Entity<EmailDeliveryJob>());
        ConfigureAuditEntity<ApplicationNotification>(modelBuilder.Entity<ApplicationNotification>());
        ConfigureAuditEntity<SalesOrder>(modelBuilder.Entity<SalesOrder>());
        ConfigureAuditEntity<Payment>(modelBuilder.Entity<Payment>());
        ConfigureAuditEntity<ProductTransaction>(modelBuilder.Entity<ProductTransaction>());
        ConfigureAuditEntity<PurchaseRecord>(modelBuilder.Entity<PurchaseRecord>());

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

        modelBuilder.Entity<CompanyProfile>(entity =>
        {
            entity.Property(x => x.CompanyName).HasMaxLength(180);
            entity.Property(x => x.Address).HasMaxLength(500);
            entity.Property(x => x.City).HasMaxLength(100);
            entity.Property(x => x.District).HasMaxLength(100);
            entity.Property(x => x.State).HasMaxLength(100);
            entity.Property(x => x.PostalCode).HasMaxLength(15);
            entity.Property(x => x.Phone).HasMaxLength(25);
            entity.Property(x => x.Email).HasMaxLength(254);
            entity.Property(x => x.GstNumber).HasMaxLength(32);
            entity.Property(x => x.BankName).HasMaxLength(150);
            entity.Property(x => x.BankAccountName).HasMaxLength(150);
            entity.Property(x => x.BankAccountNumber).HasMaxLength(64);
            entity.Property(x => x.BankIfscCode).HasMaxLength(20);
            entity.Property(x => x.BankBranch).HasMaxLength(150);
            entity.Property(x => x.UpiId).HasMaxLength(100);
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
            entity.Property(x => x.OpeningBalance).HasPrecision(18, 2).HasDefaultValue(0m);
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

        modelBuilder.Entity<RawMaterial>(entity =>
        {
            entity.Property(x => x.Name).HasMaxLength(180).IsRequired();
            entity.Property(x => x.Unit).HasMaxLength(30).HasDefaultValue("kg");
            entity.Property(x => x.Specification).HasMaxLength(300);
            entity.Property(x => x.TotalStock).HasPrecision(18, 3).HasDefaultValue(0m);
            entity.Property(x => x.UsedStock).HasPrecision(18, 3).HasDefaultValue(0m);
            entity.Property(x => x.AvailableStock).HasPrecision(18, 3).HasComputedColumnSql("[TotalStock] - [UsedStock]");
            entity.Property(x => x.IsActive).HasDefaultValue(true);
        });

        modelBuilder.Entity<ProductRawMaterial>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ConsumptionQuantity).HasPrecision(18, 3).IsRequired();

            entity.HasOne(x => x.Product)
                .WithMany(x => x.RawMaterials)
                .HasForeignKey(x => x.ProductId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(x => x.RawMaterial)
                .WithMany(x => x.ProductRawMaterials)
                .HasForeignKey(x => x.RawMaterialId)
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

        modelBuilder.Entity<ProductTransaction>(entity =>
        {
            entity.HasIndex(x => x.TransactionNumber).IsUnique();
            entity.Property(x => x.TransactionNumber).HasMaxLength(48).IsRequired();
            entity.Property(x => x.TransactionType).HasMaxLength(30).IsRequired();
            entity.Property(x => x.PartyName).HasMaxLength(150);
            entity.Property(x => x.PartyMobile).HasMaxLength(25);
            entity.Property(x => x.PartyLocation).HasMaxLength(200);
            entity.Property(x => x.Notes).HasMaxLength(2000);
            entity.Property(x => x.Quantity).HasPrecision(18, 3);
            entity.Property(x => x.Amount).HasPrecision(18, 2);
            entity.Property(x => x.PaymentStatus).HasMaxLength(30).IsRequired();
            entity.HasOne(x => x.Product)
                .WithMany(x => x.ProductTransactions)
                .HasForeignKey(x => x.ProductId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PurchaseRecord>(entity =>
        {
            entity.HasIndex(x => x.PurchaseNumber).IsUnique();
            entity.HasIndex(x => x.PurchaseDate);
            entity.Property(x => x.PurchaseNumber).HasMaxLength(48).IsRequired();
            entity.Property(x => x.ProductName).HasMaxLength(300).IsRequired();
            entity.Property(x => x.ProductCode).HasMaxLength(50);
            entity.Property(x => x.BuyerName).HasMaxLength(180);
            entity.Property(x => x.BuyerContactNumber).HasMaxLength(25);
            entity.Property(x => x.BuyerGstNumber).HasMaxLength(32);
            entity.Property(x => x.BuyerLocation).HasMaxLength(500);
            entity.Property(x => x.SupplierName).HasMaxLength(180);
            entity.Property(x => x.PurchaseDate).HasColumnType("date");
            entity.Property(x => x.QuantityPurchased).HasPrecision(18, 3);
            entity.Property(x => x.PurchaseAmount).HasPrecision(18, 2);
            entity.Property(x => x.GstAmount).HasPrecision(18, 2);
            entity.Property(x => x.GstRate).HasPrecision(5, 2);
            entity.Property(x => x.PaymentStatus).HasMaxLength(30).IsRequired();
            entity.Property(x => x.Notes).HasMaxLength(2000);
        });
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        await ProcessStockChanges(cancellationToken);
        StampAuditFields();
        return await base.SaveChangesAsync(cancellationToken);
    }

    public override int SaveChanges()
    {
        StampAuditFields();
        return base.SaveChanges();
    }

    private async Task ProcessStockChanges(CancellationToken cancellationToken)
    {
        var orderEntries = ChangeTracker.Entries<SalesOrder>()
            .Where(e => e.State == EntityState.Modified || e.State == EntityState.Deleted)
            .ToList();
        var itemEntries = ChangeTracker.Entries<SalesOrderItem>()
            .Where(e => e.State == EntityState.Added || e.State == EntityState.Deleted)
            .ToList();

        foreach (var entry in orderEntries)
        {
            var orderId = entry.Entity.Id;
            var oldStatus = (string)entry.OriginalValues["Status"]!;
            if (entry.State == EntityState.Deleted)
            {
                // Entity Framework cascades the line-item deletes in the
                // database, so restore stock before the order disappears.
                if (!IsCancelled(oldStatus))
                {
                    await ApplyPersistedOrderItemStockAsync(orderId, consumeStock: false, cancellationToken);
                }

                continue;
            }

            var newStatus = (string)entry.CurrentValues["Status"]!;

            if (!IsCancelled(oldStatus) && IsCancelled(newStatus))
            {
                // Use persisted lines: a full update may replace the requested
                // lines while cancelling, but a cancelled order must return the
                // stock consumed by its original active version.
                await ApplyPersistedOrderItemStockAsync(orderId, consumeStock: false, cancellationToken);
            }
            else if (IsCancelled(oldStatus) && !IsCancelled(newStatus))
            {
                var replacementItems = itemEntries
                    .Where(item => item.State == EntityState.Added && item.Entity.SalesOrderId == orderId)
                    .ToList();
                if (replacementItems.Count == 0)
                {
                    // A status-only reactivation consumes the saved lines.
                    await ApplyPersistedOrderItemStockAsync(orderId, consumeStock: true, cancellationToken);
                }
                else
                {
                    // A full update replaces the cancelled lines. Consume the
                    // new values, not the old database rows.
                    foreach (var item in replacementItems)
                    {
                        await ApplyStockDeltaAsync(
                            item.Entity.ProductId,
                            -item.Entity.Quantity,
                            item.Entity.Quantity,
                            cancellationToken);
                    }
                }
            }
        }

        foreach (var entry in itemEntries)
        {
            var item = entry.Entity;
            Guid? productId = entry.State == EntityState.Deleted
                ? entry.OriginalValues["ProductId"] is Guid originalProductId ? originalProductId : null
                : item.ProductId;
            var quantity = entry.State == EntityState.Deleted ? (decimal)entry.OriginalValues["Quantity"]! : item.Quantity;

            var orderId = entry.State == EntityState.Deleted ? (Guid)entry.OriginalValues["SalesOrderId"]! : item.SalesOrderId;

            var orderEntry = ChangeTracker.Entries<SalesOrder>().FirstOrDefault(e => e.Entity.Id == orderId);
            string orderStatus = "Pending";

            if (orderEntry != null)
            {
                orderStatus = orderEntry.State == EntityState.Deleted ? "Deleted" : (string)orderEntry.CurrentValues["Status"]!;
                if (orderEntry.State == EntityState.Modified)
                {
                    string oldStatus = (string)orderEntry.OriginalValues["Status"]!;
                    string newStatus = (string)orderEntry.CurrentValues["Status"]!;
                    if (oldStatus != newStatus && (IsCancelled(oldStatus) || IsCancelled(newStatus)))
                    {
                        // Status-transition stock was handled from the
                        // appropriate old or replacement lines above.
                        continue;
                    }
                }
            }
            else
            {
                var order = await SalesOrders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken);
                if (order != null) orderStatus = order.Status;
            }

            if (IsCancelled(orderStatus) || orderStatus == "Deleted" || !productId.HasValue) continue;

            if (entry.State == EntityState.Added)
            {
                await ApplyStockDeltaAsync(productId, -quantity, quantity, cancellationToken);
            }
            else if (entry.State == EntityState.Deleted)
            {
                await ApplyStockDeltaAsync(productId, quantity, -quantity, cancellationToken);
            }
        }
    }

    private async Task ApplyPersistedOrderItemStockAsync(
        Guid orderId,
        bool consumeStock,
        CancellationToken cancellationToken)
    {
        var items = await SalesOrderItems
            .AsNoTracking()
            .Where(item => item.SalesOrderId == orderId)
            .Select(item => new { item.ProductId, item.Quantity })
            .ToListAsync(cancellationToken);
        foreach (var item in items)
        {
            var quantityOnHandDelta = consumeStock ? -item.Quantity : item.Quantity;
            var totalSoldDelta = consumeStock ? item.Quantity : -item.Quantity;
            await ApplyStockDeltaAsync(item.ProductId, quantityOnHandDelta, totalSoldDelta, cancellationToken);
        }
    }

    private async Task ApplyStockDeltaAsync(
        Guid? productId,
        decimal quantityOnHandDelta,
        decimal totalSoldDelta,
        CancellationToken cancellationToken)
    {
        if (!productId.HasValue)
        {
            return;
        }

        var product = await Products.Include(p => p.RawMaterials).FirstOrDefaultAsync(p => p.Id == productId.Value, cancellationToken);
        if (product is null)
        {
            return;
        }

        product.QuantityOnHand += quantityOnHandDelta;
        product.TotalSold += totalSoldDelta;

        if (totalSoldDelta != 0m && product.RawMaterials != null && product.RawMaterials.Count > 0)
        {
            var rawMaterialIds = product.RawMaterials.Select(prm => prm.RawMaterialId).ToList();
            var rawMaterials = await RawMaterials.Where(rm => rawMaterialIds.Contains(rm.Id)).ToListAsync(cancellationToken);
            foreach (var prm in product.RawMaterials)
            {
                var rm = rawMaterials.FirstOrDefault(r => r.Id == prm.RawMaterialId);
                if (rm != null)
                {
                    rm.UsedStock += (prm.ConsumptionQuantity * totalSoldDelta);
                }
            }
        }
    }

    private static bool IsCancelled(string status) =>
        string.Equals(status, "Cancelled", StringComparison.OrdinalIgnoreCase);

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

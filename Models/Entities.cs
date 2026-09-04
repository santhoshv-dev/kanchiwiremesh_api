namespace KanchimeshAPI.Models;

public abstract class AuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public byte[]? RowVersion { get; set; }
}

public static class ApplicationRoles
{
    public const string Administrator = "Administrator";
}

public sealed class ApplicationUser : AuditableEntity
{
    public string Email { get; set; } = string.Empty;
    public string NormalizedEmail { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Role { get; set; } = ApplicationRoles.Administrator;
    public string PasswordHash { get; set; } = string.Empty;
    // Kept for response compatibility with already-released clients. Password
    // changes are optional; sign-in must never be held behind this flag.
    public bool MustChangePassword { get; set; } = false;
    public bool IsActive { get; set; } = true;
    public DateTime? LastLoginAtUtc { get; set; }
}

/// <summary>
/// The single, business-wide profile shown on invoices. A fixed identifier is
/// used so the application can safely create it on the first settings save
/// without needing a separate tenant or user-specific settings model.
/// </summary>
public sealed class CompanyProfile : AuditableEntity
{
    public static readonly Guid DefaultId = new("6DDB0D40-9EC2-4F3D-9ED2-4EF2D40B4A01");

    public string? CompanyName { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? District { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? GstNumber { get; set; }

    public string? BankName { get; set; }
    public string? BankAccountName { get; set; }
    public string? BankAccountNumber { get; set; }
    public string? BankIfscCode { get; set; }
    public string? BankBranch { get; set; }
    public string? UpiId { get; set; }
}

public sealed class Customer : AuditableEntity
{
    public string CustomerCode { get; set; } = string.Empty;
    public string ContactName { get; set; } = string.Empty;
    public string? CompanyName { get; set; }
    public string Phone { get; set; } = string.Empty;
    public string? AlternatePhone { get; set; }
    public string? WhatsAppNumber { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? District { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string? GstNumber { get; set; }
    public string? BusinessType { get; set; }
    public string? Notes { get; set; }
    public decimal OpeningBalance { get; set; }
    public bool IsActive { get; set; } = true;

    public ICollection<Enquiry> Enquiries { get; set; } = new List<Enquiry>();
    public ICollection<SalesOrder> Orders { get; set; } = new List<SalesOrder>();
    public ICollection<Payment> Payments { get; set; } = new List<Payment>();
}

public sealed class Product : AuditableEntity
{
    public string ProductCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string HsnSac { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string? MeshType { get; set; }
    public string? MeshOpening { get; set; }
    public string? WireDiameter { get; set; }
    public decimal? Width { get; set; }
    public decimal? Length { get; set; }
    public string Unit { get; set; } = "pcs";
    public decimal Rate { get; set; }
    public decimal IgstRate { get; set; } = 18m; public decimal SgstRate { get; set; } = 9m; public decimal CgstRate { get; set; } = 9m;
    public decimal QuantityOnHand { get; set; }
    public decimal ReorderLevel { get; set; }
    public string? Description { get; set; }
    public decimal TotalStockAdded { get; set; }
    public decimal TotalSold { get; set; }
    public bool IsActive { get; set; } = true;

    public ICollection<SalesOrderItem> OrderItems { get; set; } = new List<SalesOrderItem>();
    public ICollection<ProductTransaction> ProductTransactions { get; set; } = new List<ProductTransaction>();
    public ICollection<ProductRawMaterial> RawMaterials { get; set; } = new List<ProductRawMaterial>();
}

public sealed class Enquiry : AuditableEntity
{
    public string EnquiryNumber { get; set; } = string.Empty;
    public string? PublicSubmissionKey { get; set; }
    public Guid? CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public string ContactName { get; set; } = string.Empty;
    public string? CompanyName { get; set; }
    public string Phone { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? ProductRequirement { get; set; }
    public decimal? Quantity { get; set; }
    public string? Unit { get; set; }
    public string? Message { get; set; }
    public string? Note { get; set; }
    public string Status { get; set; } = "New";
    public DateOnly? FollowUpDate { get; set; }
    public string EmailDeliveryStatus { get; set; } = EmailDeliveryStatuses.NotRequested;
    public DateTime? EmailDeliveryAttemptedAtUtc { get; set; }

    public ICollection<EmailDeliveryJob> EmailDeliveryJobs { get; set; } = new List<EmailDeliveryJob>();
}

public sealed class EmailDeliveryJob : AuditableEntity
{
    public Guid EnquiryId { get; set; }
    public Enquiry Enquiry { get; set; } = null!;
    public string Kind { get; set; } = EmailDeliveryJobKinds.CustomerConfirmation;
    public string Recipient { get; set; } = string.Empty;
    public string Status { get; set; } = EmailDeliveryJobStatuses.Pending;
    public int AttemptCount { get; set; }
    public DateTime NextAttemptAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? LockedUntilUtc { get; set; }
    public DateTime? LastAttemptAtUtc { get; set; }
    public DateTime? SentAtUtc { get; set; }
    public string? LastError { get; set; }
}

public static class NotificationTypes
{
    public const string EnquiryReceived = "EnquiryReceived";
}

public sealed class ApplicationNotification : AuditableEntity
{
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public Guid? RelatedEnquiryId { get; set; }
    public Guid? RelatedCustomerId { get; set; }
    public bool IsRead { get; set; }
    public DateTime? ReadAtUtc { get; set; }
}

public sealed class SalesOrder : AuditableEntity
{
    public string OrderNumber { get; set; } = string.Empty;
    public Guid CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;
    public DateOnly OrderDate { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow);
    public DateOnly? ExpectedDeliveryDate { get; set; }
    public string Status { get; set; } = "Pending";
    public string? Notes { get; set; }
    public decimal Subtotal { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal FreightAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public string GstType { get; set; } = "IGST";
    public decimal GrandTotal { get; set; }

    public ICollection<SalesOrderItem> Items { get; set; } = new List<SalesOrderItem>();
    public ICollection<Payment> Payments { get; set; } = new List<Payment>();
}

public sealed class SalesOrderItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SalesOrderId { get; set; }
    public SalesOrder SalesOrder { get; set; } = null!;
    public Guid? ProductId { get; set; }
    public Product? Product { get; set; }
    public string Description { get; set; } = string.Empty;
    public string? HsnSac { get; set; }
    public string? Specification { get; set; }
    public decimal Quantity { get; set; }
    public string Unit { get; set; } = "pcs";
    public decimal Rate { get; set; }
    public decimal IgstRate { get; set; }
    public decimal SgstRate { get; set; }
    public decimal CgstRate { get; set; }
    public decimal LineSubtotal { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal LineTotal { get; set; }
}

public sealed class Payment : AuditableEntity
{
    public string PaymentNumber { get; set; } = string.Empty;
    public Guid CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;
    public Guid? SalesOrderId { get; set; }
    public SalesOrder? SalesOrder { get; set; }
    public decimal Amount { get; set; }
    public DateOnly PaymentDate { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow);
    public string Method { get; set; } = "UPI";
    public string? Reference { get; set; }
    public string? Notes { get; set; }
    public bool IsAdvance { get; set; }
}


public sealed class ProductTransaction : AuditableEntity
{
    public string TransactionNumber { get; set; } = string.Empty;
    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public string TransactionType { get; set; } = "Sale";

    public string? PartyName { get; set; }
    public string? PartyMobile { get; set; }
    public string? PartyLocation { get; set; }

    public DateOnly TransactionDate { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow);
    public decimal Quantity { get; set; }
    public decimal Amount { get; set; }
    public string PaymentStatus { get; set; } = "Paid";
    public string? Notes { get; set; }
}

public sealed class PurchaseRecord : AuditableEntity
{
    public string PurchaseNumber { get; set; } = string.Empty;

    public string ProductName { get; set; } = string.Empty;
    public string? ProductCode { get; set; }

    public string? BuyerName { get; set; }
    public string? BuyerContactNumber { get; set; }
    public string? BuyerGstNumber { get; set; }
    public string? BuyerLocation { get; set; }
    public string? SupplierName { get; set; }

    public DateOnly PurchaseDate { get; set; }
    public decimal QuantityPurchased { get; set; }
    public decimal PurchaseAmount { get; set; }
    public decimal? GstAmount { get; set; }
    public decimal? GstRate { get; set; }
    public string PaymentStatus { get; set; } = "Pending";
    public string? Notes { get; set; }
}

public sealed class RawMaterial : AuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public decimal TotalStock { get; set; }
    public decimal UsedStock { get; set; }
    public decimal AvailableStock { get; set; }
    public bool IsActive { get; set; } = true;

    public ICollection<ProductRawMaterial> ProductRawMaterials { get; set; } = new List<ProductRawMaterial>();
}

public sealed class ProductRawMaterial
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public Guid RawMaterialId { get; set; }
    public RawMaterial RawMaterial { get; set; } = null!;
    public decimal ConsumptionQuantity { get; set; }
}

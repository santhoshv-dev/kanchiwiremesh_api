using System.ComponentModel.DataAnnotations;
using KanchimeshAPI.Models;

namespace KanchimeshAPI.DTOs;

public sealed class CustomerRequest
{
    [Required, StringLength(150)]
    public string ContactName { get; init; } = string.Empty;

    [StringLength(180)]
    public string? CompanyName { get; init; }

    // Accepted for compatibility with the existing Flutter wireframe payload.
    [StringLength(180)]
    public string? Company { get; init; }

    [Required, StringLength(25, MinimumLength = 7)]
    public string Phone { get; init; } = string.Empty;

    [StringLength(25)] public string? AlternatePhone { get; init; }
    [StringLength(25)] public string? WhatsAppNumber { get; init; }
    [EmailAddress, StringLength(254)] public string? Email { get; init; }
    [StringLength(500)] public string? Address { get; init; }
    [StringLength(100)] public string? City { get; init; }
    [StringLength(100)] public string? District { get; init; }
    [StringLength(100)] public string? State { get; init; }
    [StringLength(15)] public string? PostalCode { get; init; }
    [StringLength(32)]
    [RegularExpression(
        @"^[0-9]{2}[A-Za-z]{5}[0-9]{4}[A-Za-z][1-9A-Za-z][Zz][0-9A-Za-z]$",
        ErrorMessage = "GSTIN must be a valid 15-character Indian GST identification number.")]
    public string? GstNumber { get; init; }
    [StringLength(100)] public string? BusinessType { get; init; }
    [StringLength(2000)] public string? Notes { get; init; }
    public bool IsActive { get; init; } = true;
}

public sealed class ProductRequest
{
    [Required, StringLength(180)] public string Name { get; init; } = string.Empty;
    [Required, StringLength(100)] public string Category { get; init; } = string.Empty;
    [StringLength(100)] public string? MeshType { get; init; }
    [StringLength(100)] public string? MeshOpening { get; init; }
    [StringLength(100)] public string? WireDiameter { get; init; }
    [Range(typeof(decimal), "0.001", "999999999999999")] public decimal? Width { get; init; }
    [Range(typeof(decimal), "0.001", "999999999999999")] public decimal? Length { get; init; }
    [Required, StringLength(30)] public string Unit { get; init; } = "pcs";
    [Range(typeof(decimal), "0", "999999999999999")] public decimal Rate { get; init; }
    [Range(typeof(decimal), "0", "28")] public decimal GstRate { get; init; } = 18m;
    [Range(typeof(decimal), "0", "999999999999999")] public decimal ReorderLevel { get; init; }
    // Applied only when a product is first created. Later changes must use the
    // stock adjustment endpoint so the inventory ledger remains complete.
    [Range(typeof(decimal), "0", "999999999999999")] public decimal? InitialStock { get; init; }
    [StringLength(2000)] public string? Description { get; init; }
    public bool IsActive { get; init; } = true;
}

public sealed class StockAdjustmentRequest
{
    [Range(typeof(decimal), "-999999999999999", "999999999999999")]
    public decimal QuantityChange { get; init; }

    [Required, StringLength(30)]
    public string MovementType { get; init; } = StockMovementTypes.Adjustment;

    [StringLength(500)] public string? Reason { get; init; }
    [StringLength(150)] public string? Reference { get; init; }
    public DateTime? OccurredAtUtc { get; init; }
}

public sealed class EnquiryRequest
{
    public Guid? CustomerId { get; init; }
    [Required, StringLength(150)] public string ContactName { get; init; } = string.Empty;
    [StringLength(180)] public string? CompanyName { get; init; }
    [Required, StringLength(25, MinimumLength = 7)] public string Phone { get; init; } = string.Empty;
    [EmailAddress, StringLength(254)] public string? Email { get; init; }
    [StringLength(300)] public string? ProductRequirement { get; init; }
    [Range(typeof(decimal), "0.001", "999999999999999")] public decimal? Quantity { get; init; }
    [StringLength(30)] public string? Unit { get; init; }
    [StringLength(4000)] public string? Message { get; init; }
    [StringLength(2000)] public string? Note { get; init; }
    [Required, StringLength(30)] public string Status { get; init; } = "New";
    public DateOnly? FollowUpDate { get; init; }
}

/// <summary>
/// Fields accepted from the public contact form. Internal workflow, customer
/// linking, notes, and follow-up scheduling are intentionally excluded.
/// </summary>
public sealed class PublicEnquiryRequest
{
    [Required, StringLength(150)] public string ContactName { get; init; } = string.Empty;
    [StringLength(180)] public string? CompanyName { get; init; }
    [Required, StringLength(25, MinimumLength = 7)] public string Phone { get; init; } = string.Empty;
    [Required, EmailAddress, StringLength(254)] public string Email { get; init; } = string.Empty;
    [StringLength(300)] public string? ProductRequirement { get; init; }
    [Range(typeof(decimal), "0.001", "999999999999999")] public decimal? Quantity { get; init; }
    [StringLength(30)] public string? Unit { get; init; }
    [StringLength(4000)] public string? Message { get; init; }
}

public sealed class OrderItemRequest
{
    public Guid? ProductId { get; init; }
    [Required, StringLength(300)] public string Description { get; init; } = string.Empty;
    [StringLength(1000)] public string? Specification { get; init; }
    [Range(typeof(decimal), "0.001", "999999999999999")] public decimal Quantity { get; init; }
    [Required, StringLength(30)] public string Unit { get; init; } = "pcs";
    [Range(typeof(decimal), "0", "999999999999999")] public decimal Rate { get; init; }
    [Range(typeof(decimal), "0", "28")] public decimal GstRate { get; init; } = 18m;
}

public sealed class OrderRequest
{
    // A full API client sends customerId/items. The existing Flutter wireframe sends customerName/productName/amount.
    public Guid CustomerId { get; set; }
    [StringLength(180)] public string? CustomerName { get; init; }
    [StringLength(300)] public string? ProductName { get; init; }
    [Range(typeof(decimal), "0.01", "999999999999999")] public decimal? Amount { get; init; }
    [Range(typeof(decimal), "0", "999999999999999")] public decimal? PaidAmount { get; init; }
    public DateTime? Date { get; init; }
    public DateOnly OrderDate { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow);
    public DateOnly? ExpectedDeliveryDate { get; init; }
    [Required, StringLength(30)] public string Status { get; init; } = "New";
    [StringLength(2000)] public string? Notes { get; init; }
    [Range(typeof(decimal), "0", "999999999999999")] public decimal DiscountAmount { get; init; }
    [Range(typeof(decimal), "0", "999999999999999")] public decimal FreightAmount { get; init; }
    public List<OrderItemRequest> Items { get; set; } = [];
}

public sealed class OrderStatusRequest
{
    [Required, StringLength(30)] public string Status { get; init; } = string.Empty;
}

public sealed class PaymentRequest
{
    // customerName/orderId/date are compatibility aliases for the Flutter wireframe client.
    public Guid CustomerId { get; set; }
    [StringLength(180)] public string? CustomerName { get; init; }
    public Guid? SalesOrderId { get; set; }
    [StringLength(64)] public string? OrderId { get; init; }
    [Range(typeof(decimal), "0.01", "999999999999999")] public decimal Amount { get; init; }
    public DateOnly PaymentDate { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow);
    public DateTime? Date { get; init; }
    [Required, StringLength(30)] public string Method { get; init; } = "UPI";
    [StringLength(150)] public string? Reference { get; init; }
    [StringLength(2000)] public string? Notes { get; init; }
    public bool IsAdvance { get; init; }
}

public sealed class LoginRequest
{
    [Required, EmailAddress, StringLength(254)] public string EmailOrPhone { get; init; } = string.Empty;
    [Required, StringLength(128)] public string Password { get; init; } = string.Empty;
}

public sealed class ChangePasswordRequest
{
    [Required, StringLength(128)] public string CurrentPassword { get; init; } = string.Empty;
    [Required, StringLength(128, MinimumLength = 8)] public string NewPassword { get; init; } = string.Empty;
}

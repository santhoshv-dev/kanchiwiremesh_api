using System.ComponentModel.DataAnnotations;

namespace KanchimeshAPI.DTOs;

public sealed class QuotationItemRequest
{
    public Guid? ProductId { get; init; }
    [Required, StringLength(250)] public string Description { get; init; } = string.Empty;
    [StringLength(20)] public string? HsnSac { get; init; }
    [StringLength(200)] public string? Specification { get; init; }
    [Range(typeof(decimal), "0.001", "999999999999")] public decimal Quantity { get; init; }
    [Required, StringLength(20)] public string Unit { get; init; } = "pcs";
    [Range(typeof(decimal), "0", "999999999999")] public decimal Rate { get; init; }
    [Range(typeof(decimal), "0", "100")] public decimal? IgstRate { get; init; }
    [Range(typeof(decimal), "0", "100")] public decimal? SgstRate { get; init; }
    [Range(typeof(decimal), "0", "100")] public decimal? CgstRate { get; init; }
}

public sealed class QuotationRequest
{
    public Guid? CustomerId { get; init; }
    [StringLength(180)] public string? CustomerName { get; init; }
    [StringLength(25)] public string? CustomerPhone { get; init; }
    [StringLength(254)] public string? CustomerEmail { get; init; }
    [StringLength(500)] public string? CustomerAddress { get; init; }
    [StringLength(32)] public string? CustomerGstNumber { get; init; }

    public DateOnly QuotationDate { get; init; } = DateOnly.FromDateTime(DateTime.UtcNow);
    public DateOnly? ValidUntilDate { get; init; }
    [Required, StringLength(30)] public string Status { get; init; } = "Draft";
    [StringLength(2000)] public string? Notes { get; init; }
    [StringLength(4000)] public string? TermsAndConditions { get; init; }
    [StringLength(20)] public string GstType { get; init; } = "IGST";
    [Range(typeof(decimal), "0", "999999999999999")] public decimal DiscountAmount { get; init; }
    [Range(typeof(decimal), "0", "999999999999999")] public decimal FreightAmount { get; init; }

    public List<QuotationItemRequest> Items { get; init; } = [];
}

public sealed class QuotationStatusRequest
{
    [Required, StringLength(30)] public string Status { get; init; } = string.Empty;
}

public sealed record QuotationItemDto(
    Guid Id,
    Guid? ProductId,
    string? ProductCode,
    string Description,
    string? HsnSac,
    string? Specification,
    decimal Quantity,
    string Unit,
    decimal Rate,
    decimal IgstRate,
    decimal SgstRate,
    decimal CgstRate,
    decimal LineSubtotal,
    decimal TaxAmount,
    decimal LineTotal
);

public sealed record QuotationSummaryDto(
    Guid Id,
    string QuotationNumber,
    Guid? CustomerId,
    string CustomerName,
    string? CustomerPhone,
    DateOnly QuotationDate,
    DateOnly? ValidUntilDate,
    string Status,
    string GstType,
    decimal Subtotal,
    decimal DiscountAmount,
    decimal FreightAmount,
    decimal TaxAmount,
    decimal GrandTotal,
    int ItemCount,
    Guid? ConvertedSalesOrderId,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    IReadOnlyList<QuotationItemDto> Items
);

public sealed record QuotationDetailDto(
    Guid Id,
    string QuotationNumber,
    Guid? CustomerId,
    string CustomerName,
    string? CustomerPhone,
    string? CustomerEmail,
    string? CustomerAddress,
    string? CustomerGstNumber,
    DateOnly QuotationDate,
    DateOnly? ValidUntilDate,
    string Status,
    string GstType,
    decimal Subtotal,
    decimal DiscountAmount,
    decimal FreightAmount,
    decimal TaxAmount,
    decimal GrandTotal,
    string? Notes,
    string? TermsAndConditions,
    Guid? ConvertedSalesOrderId,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    IReadOnlyList<QuotationItemDto> Items,
    CompanyProfileDto? Company = null
);

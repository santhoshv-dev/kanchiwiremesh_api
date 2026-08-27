namespace KanchimeshAPI.DTOs;

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
}

public sealed record CustomerListItemDto(
    Guid Id,
    string CustomerCode,
    string ContactName,
    string? CompanyName,
    string Phone,
    string? City,
    bool IsActive,
    int OrderCount,
    decimal TotalSales,
    decimal TotalPaid,
    decimal Outstanding);

public sealed record CustomerDetailDto(
    Guid Id,
    string CustomerCode,
    string ContactName,
    string? CompanyName,
    string Phone,
    string? AlternatePhone,
    string? WhatsAppNumber,
    string? Email,
    string? Address,
    string? City,
    string? District,
    string? State,
    string? PostalCode,
    string? GstNumber,
    string? BusinessType,
    string? Notes,
    bool IsActive,
    int OrderCount,
    decimal TotalSales,
    decimal TotalPaid,
    decimal Outstanding,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record ProductDto(
    Guid Id,
    string ProductCode,
    string Name,
    string Category,
    string? MeshType,
    string? MeshOpening,
    string? WireDiameter,
    decimal? Width,
    decimal? Length,
    string Unit,
    decimal Rate,
    decimal IgstRate, decimal SgstRate, decimal CgstRate,
    decimal QuantityOnHand,
    decimal TotalStockAdded,
    decimal TotalSold,
    decimal ReorderLevel,
    bool IsLowStock,
    bool IsOutOfStock,
    string? Description,
    bool IsActive,
    DateTime UpdatedAtUtc);

public sealed record InventorySummaryDto(
    Guid ProductId,
    string ProductCode,
    string ProductName,
    string Category,
    string Unit,
    decimal QuantityOnHand,
    decimal ReorderLevel,
    bool IsLowStock,
    bool IsOutOfStock,
    DateTime UpdatedAtUtc);

public sealed record EnquiryDto(
    Guid Id,
    string EnquiryNumber,
    Guid? CustomerId,
    string? CustomerName,
    string ContactName,
    string? CompanyName,
    string Phone,
    string? Email,
    string? ProductRequirement,
    decimal? Quantity,
    string? Unit,
    string? Message,
    string? Note,
    string Status,
    DateOnly? FollowUpDate,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    string EmailDeliveryStatus,
    DateTime? EmailDeliveryAttemptedAtUtc);

public sealed record PublicEnquiryResponse(
    string EnquiryNumber,
    string EmailDeliveryStatus,
    bool ConfirmationEmailSent,
    string Message);

public sealed record CompanyProfileDto(
    string? CompanyName,
    string? Address,
    string? City,
    string? District,
    string? State,
    string? PostalCode,
    string? Phone,
    string? Email,
    string? GstNumber,
    string? BankName,
    string? BankAccountName,
    string? BankAccountNumber,
    string? BankIfscCode,
    string? BankBranch,
    string? UpiId,
    DateTime? UpdatedAtUtc);

public sealed record OrderItemDto(
    Guid Id,
    Guid? ProductId,
    string Description,
    string? Specification,
    decimal Quantity,
    string Unit,
    decimal Rate,
    decimal IgstRate, decimal SgstRate, decimal CgstRate,
    decimal LineSubtotal,
    decimal TaxAmount,
    decimal LineTotal);

public sealed record OrderSummaryDto(
    Guid Id,
    string OrderNumber,
    Guid CustomerId,
    string CustomerName,
    string ProductName,
    DateOnly OrderDate,
    DateOnly? ExpectedDeliveryDate,
    string Status,
    decimal GrandTotal,
    decimal PaidAmount,
    decimal Outstanding,
    DateTime UpdatedAtUtc);

public sealed record OrderDetailDto(
    Guid Id,
    string OrderNumber,
    Guid CustomerId,
    string CustomerName,
    string ProductName,
    DateOnly OrderDate,
    DateOnly? ExpectedDeliveryDate,
    string Status,
    string? Notes,
    decimal Subtotal,
    decimal DiscountAmount,
    decimal FreightAmount,
    decimal TaxAmount,
    string GstType,
    decimal GrandTotal,
    decimal PaidAmount,
    decimal Outstanding,
    IReadOnlyList<OrderItemDto> Items,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    CompanyProfileDto? Company = null);

public sealed record PaymentDto(
    Guid Id,
    string PaymentNumber,
    Guid CustomerId,
    string CustomerName,
    Guid? SalesOrderId,
    Guid? OrderId,
    string? OrderNumber,
    decimal Amount,
    DateOnly PaymentDate,
    string Method,
    string? Reference,
    string? Notes,
    bool IsAdvance,
    DateTime CreatedAtUtc);

public sealed record LedgerTransactionDto(
    DateOnly Date,
    string Type,
    string Description,
    decimal Debit,
    decimal Credit,
    decimal Balance,
    Guid? ReferenceId);

public sealed record CustomerLedgerDto(
    Guid CustomerId,
    string CustomerName,
    decimal TotalSales,
    decimal TotalPaid,
    decimal Outstanding,
    IReadOnlyList<LedgerTransactionDto> Transactions);

public sealed record NotificationDto(
    Guid Id,
    string Title,
    string Message,
    string Type,
    Guid? RelatedEnquiryId,
    Guid? RelatedCustomerId,
    DateTime CreatedAtUtc,
    bool IsRead,
    DateTime? ReadAtUtc);

public sealed record UnreadNotificationCountDto(int UnreadCount);

public sealed record MarkNotificationsReadResultDto(int UpdatedCount);

public sealed record DashboardSummaryDto(
    int CustomerCount,
    int ActiveProductCount,
    int NewEnquiryCount,
    int PendingOrderCount,
    int CompletedOrderCount,
    decimal TotalSales,
    decimal TotalReceived,
    decimal Received,
    decimal Outstanding,
    decimal AdvanceBalance,
    IReadOnlyList<OrderSummaryDto> RecentOrders,
    IReadOnlyList<decimal> SalesBars);

public sealed record PaymentSummaryDto(
    decimal TotalSales,
    decimal TotalReceived,
    decimal Outstanding,
    decimal TotalAdvance,
    decimal AppliedToOrders);

public sealed record LoginResponseDto(
    string AccessToken,
    string TokenType,
    string DisplayName,
    string Role,
    bool MustChangePassword,
    DateTime ExpiresAtUtc);

public sealed record AuthenticatedUserDto(
    Guid Id,
    string Email,
    string DisplayName,
    string Role,
    bool MustChangePassword);

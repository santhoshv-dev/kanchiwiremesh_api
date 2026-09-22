using KanchimeshAPI.Models;

namespace KanchimeshAPI.DTOs;

public static class DtoMappings
{
    public static ProductDto ToDto(this Product product) => new(
        product.Id, product.ProductCode, product.Name, product.HsnSac, product.Category, product.MeshType,
        product.MeshOpening, product.WireDiameter, product.Width, product.Length, product.Unit,
        product.Rate, product.IgstRate, product.SgstRate, product.CgstRate, product.QuantityOnHand, product.TotalStockAdded, product.TotalSold, product.ReorderLevel,
        product.QuantityOnHand <= product.ReorderLevel,
        product.QuantityOnHand <= 0m,
        product.Description, product.IsActive, product.UpdatedAtUtc,
        product.RawMaterials?.Select(rm => rm.ToDto()).ToList(),
        AvailablePieces(product), AvailablePieces(product) * product.Rate);

    private static decimal AvailablePieces(Product product) =>
        product.RawMaterials is { Count: > 0 }
            ? product.RawMaterials.Min(requirement =>
                requirement.RawMaterial is null || requirement.ConsumptionQuantity <= 0m ? 0m :
                decimal.Floor(Math.Max(0m, requirement.RawMaterial.TotalStock - requirement.RawMaterial.UsedStock)
                    / requirement.ConsumptionQuantity))
            : product.QuantityOnHand;

    public static RawMaterialDto ToDto(this RawMaterial rawMaterial) => new(
        rawMaterial.Id, rawMaterial.Name, rawMaterial.Unit ?? "kg", rawMaterial.Specification, rawMaterial.TotalStock, rawMaterial.UsedStock, rawMaterial.AvailableStock,
        rawMaterial.IsActive, rawMaterial.UpdatedAtUtc);

    public static ProductRawMaterialDto ToDto(this ProductRawMaterial prm) => new(
        prm.Id, prm.RawMaterialId, prm.RawMaterial?.Name ?? string.Empty, prm.ConsumptionQuantity, prm.RawMaterial?.Unit);

    public static PurchasePaymentDto ToDto(this PurchasePayment payment) => new(
        payment.Id,
        payment.PaymentNumber,
        payment.PurchaseRecordId,
        payment.Amount,
        payment.PaymentDate,
        payment.PaymentMode,
        payment.ReferenceNumber,
        payment.Notes,
        payment.CreatedAtUtc);

    public static PurchaseRecordDto ToDto(this PurchaseRecord purchase)
    {
        var payments = purchase.Payments?.Select(p => p.ToDto()).ToList() ?? new List<PurchasePaymentDto>();
        decimal paid;
        string status = string.IsNullOrWhiteSpace(purchase.PaymentStatus) ? "Pending" : purchase.PaymentStatus;

        if (payments.Count > 0)
        {
            paid = payments.Sum(p => p.Amount);
            if (paid >= purchase.PurchaseAmount && purchase.PurchaseAmount > 0)
            {
                status = "Fully Paid";
            }
            else if (paid > 0)
            {
                status = "Partially Paid";
            }
            else
            {
                status = "Unpaid";
            }
        }
        else if (string.Equals(purchase.PaymentStatus, "Paid", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(purchase.PaymentStatus, "Fully Paid", StringComparison.OrdinalIgnoreCase))
        {
            paid = purchase.PurchaseAmount;
            status = "Fully Paid";
        }
        else
        {
            paid = 0m;
        }

        var pending = Math.Max(0m, purchase.PurchaseAmount - paid);

        return new PurchaseRecordDto(
            purchase.Id,
            purchase.PurchaseNumber,
            purchase.ProductName,
            purchase.ProductCode,
            purchase.BuyerName,
            purchase.BuyerContactNumber,
            purchase.BuyerGstNumber,
            purchase.BuyerLocation,
            purchase.SupplierName,
            purchase.PurchaseDate,
            purchase.QuantityPurchased,
            purchase.UnitPrice,
            purchase.PurchaseAmount,
            purchase.GstAmount,
            purchase.GstRate,
            paid,
            pending,
            status,
            purchase.Notes,
            payments,
            purchase.CreatedAtUtc,
            purchase.UpdatedAtUtc);
    }

    public static ExpenseDto ToDto(this Expense expense) => new(
        expense.Id,
        expense.ExpenseNumber,
        expense.ExpenseDate,
        expense.Category,
        expense.Description,
        expense.Amount,
        expense.PaymentMode,
        expense.PaidTo,
        expense.ReferenceNumber,
        expense.Notes,
        expense.AttachmentUrl,
        expense.CreatedAtUtc,
        expense.UpdatedAtUtc);

    public static EnquiryDto ToDto(this Enquiry enquiry) => new(
        enquiry.Id, enquiry.EnquiryNumber, enquiry.CustomerId,
        enquiry.Customer is null ? null : DisplayCustomerName(enquiry.Customer), enquiry.ContactName,
        enquiry.CompanyName, enquiry.Phone, enquiry.Email, enquiry.ProductRequirement, enquiry.Quantity,
        enquiry.Unit, enquiry.Message, enquiry.Note, enquiry.Status, enquiry.FollowUpDate,
        enquiry.CreatedAtUtc, enquiry.UpdatedAtUtc, enquiry.EmailDeliveryStatus,
        enquiry.EmailDeliveryAttemptedAtUtc);

    public static OrderItemDto ToDto(this SalesOrderItem item) => new(
        item.Id, item.ProductId, item.Description, item.HsnSac, item.Specification, item.Quantity, item.Unit,
        item.Rate, item.IgstRate, item.SgstRate, item.CgstRate, item.LineSubtotal, item.TaxAmount, item.LineTotal);

    public static PaymentDto ToDto(this Payment payment) => new(
        payment.Id, payment.PaymentNumber, payment.CustomerId, DisplayCustomerName(payment.Customer),
        payment.SalesOrderId, payment.SalesOrderId, payment.SalesOrder?.OrderNumber, payment.Amount, payment.PaymentDate,
        payment.Method, payment.Reference, payment.Notes, payment.IsAdvance, payment.CreatedAtUtc);

    public static string DisplayCustomerName(Customer customer) =>
        string.IsNullOrWhiteSpace(customer.CompanyName) ? customer.ContactName : customer.CompanyName;
}

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
        product.RawMaterials?.Select(rm => rm.ToDto()).ToList());

    public static RawMaterialDto ToDto(this RawMaterial rawMaterial) => new(
        rawMaterial.Id, rawMaterial.Name, rawMaterial.TotalStock, rawMaterial.UsedStock, rawMaterial.AvailableStock,
        rawMaterial.IsActive, rawMaterial.UpdatedAtUtc);

    public static ProductRawMaterialDto ToDto(this ProductRawMaterial prm) => new(
        prm.Id, prm.RawMaterialId, prm.RawMaterial?.Name ?? string.Empty, prm.ConsumptionQuantity);

    public static PurchaseRecordDto ToDto(this PurchaseRecord purchase) => new(
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
        purchase.PurchaseAmount,
        purchase.GstAmount,
        purchase.GstRate,
        purchase.PaymentStatus,
        purchase.Notes,
        purchase.CreatedAtUtc,
        purchase.UpdatedAtUtc);

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

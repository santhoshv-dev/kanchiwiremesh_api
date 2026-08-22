using KanchimeshAPI.Models;

namespace KanchimeshAPI.DTOs;

public static class DtoMappings
{
    public static ProductDto ToDto(this Product product) => new(
        product.Id, product.ProductCode, product.Name, product.Category, product.MeshType,
        product.MeshOpening, product.WireDiameter, product.Width, product.Length, product.Unit,
        product.Rate, product.GstRate, product.QuantityOnHand, product.ReorderLevel,
        product.QuantityOnHand <= product.ReorderLevel,
        product.QuantityOnHand <= 0m,
        product.Description, product.IsActive, product.UpdatedAtUtc);

    public static EnquiryDto ToDto(this Enquiry enquiry) => new(
        enquiry.Id, enquiry.EnquiryNumber, enquiry.CustomerId,
        enquiry.Customer is null ? null : DisplayCustomerName(enquiry.Customer), enquiry.ContactName,
        enquiry.CompanyName, enquiry.Phone, enquiry.Email, enquiry.ProductRequirement, enquiry.Quantity,
        enquiry.Unit, enquiry.Message, enquiry.Note, enquiry.Status, enquiry.FollowUpDate,
        enquiry.CreatedAtUtc, enquiry.UpdatedAtUtc, enquiry.EmailDeliveryStatus,
        enquiry.EmailDeliveryAttemptedAtUtc);

    public static OrderItemDto ToDto(this SalesOrderItem item) => new(
        item.Id, item.ProductId, item.Description, item.Specification, item.Quantity, item.Unit,
        item.Rate, item.GstRate, item.LineSubtotal, item.TaxAmount, item.LineTotal);

    public static PaymentDto ToDto(this Payment payment) => new(
        payment.Id, payment.PaymentNumber, payment.CustomerId, DisplayCustomerName(payment.Customer),
        payment.SalesOrderId, payment.SalesOrderId, payment.SalesOrder?.OrderNumber, payment.Amount, payment.PaymentDate,
        payment.Method, payment.Reference, payment.Notes, payment.IsAdvance, payment.CreatedAtUtc);

    public static string DisplayCustomerName(Customer customer) =>
        string.IsNullOrWhiteSpace(customer.CompanyName) ? customer.ContactName : customer.CompanyName;
}

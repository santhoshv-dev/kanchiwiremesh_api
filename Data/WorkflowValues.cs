namespace KanchimeshAPI.Data;

public static class WorkflowValues
{
    public static readonly IReadOnlyList<string> EnquiryStatuses =
        ["New", "Contacted", "Follow Up", "Converted", "Closed"];

    public static readonly IReadOnlyList<string> OrderStatuses =
        ["Pending", "Completed", "Cancelled"];

    public static readonly IReadOnlyList<string> QuotationStatuses =
        ["Draft", "Sent", "Accepted", "Declined", "Converted", "Expired"];

    public static readonly IReadOnlyList<string> PaymentMethods =
        ["Cash", "UPI", "Bank Transfer", "Card", "Cheque", "Credit"];

    public static readonly IReadOnlyList<string> PurchasePaymentStatuses =
        ["Unpaid", "Partially Paid", "Fully Paid", "Paid", "Pending", "Partial", "Not Applicable"];

    public static readonly IReadOnlyList<string> ExpenseCategories =
        ["Rent", "Electricity & Utilities", "Salaries & Wages", "Office Supplies", "Logistics & Transport", "Machinery & Repairs", "Tea & Refreshments", "Miscellaneous"];

    public static bool TryNormalize(string? input, IReadOnlyList<string> options, out string value)
    {
        var match = options.FirstOrDefault(option => string.Equals(option, input?.Trim(), StringComparison.OrdinalIgnoreCase));
        value = match ?? string.Empty;
        return match is not null;
    }
}

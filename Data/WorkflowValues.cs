namespace KanchimeshAPI.Data;

public static class WorkflowValues
{
    public static readonly IReadOnlyList<string> EnquiryStatuses =
        ["New", "Contacted", "Follow Up", "Converted", "Closed"];

    public static readonly IReadOnlyList<string> OrderStatuses =
        ["New", "Processing", "Ready", "Dispatched", "Delivered", "Cancelled"];

    public static readonly IReadOnlyList<string> PaymentMethods =
        ["Cash", "UPI", "Bank Transfer", "Cheque", "Credit"];

    public static bool TryNormalize(string? input, IReadOnlyList<string> options, out string value)
    {
        if (ReferenceEquals(options, OrderStatuses) && string.Equals(input?.Trim(), "Ready to dispatch", StringComparison.OrdinalIgnoreCase))
        {
            value = "Ready";
            return true;
        }

        var match = options.FirstOrDefault(option => string.Equals(option, input?.Trim(), StringComparison.OrdinalIgnoreCase));
        value = match ?? string.Empty;
        return match is not null;
    }
}

namespace KanchimeshAPI.Data;

public static class DocumentNumbers
{
    // The business enquiry prefix appears consistently in the admin workspace,
    // public confirmation page, notifications, and email.
    public const string EnquiryPrefix = "ERH ORDNF";

    public static string New(string prefix) =>
        $"{prefix}-{Guid.NewGuid():N}".ToUpperInvariant();
}

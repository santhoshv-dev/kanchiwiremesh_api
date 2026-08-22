namespace KanchimeshAPI.Data;

public static class DocumentNumbers
{
    public static string New(string prefix) =>
        $"{prefix}-{Guid.NewGuid():N}".ToUpperInvariant();
}

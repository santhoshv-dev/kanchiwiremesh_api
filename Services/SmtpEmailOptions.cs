using System.Diagnostics.CodeAnalysis;

namespace KanchimeshAPI.Services;

public sealed class SmtpEmailOptions
{
    public const string SectionName = "Smtp";

    public bool Enabled { get; set; } = true;
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    public bool UseSsl { get; set; } = true;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FromAddress { get; set; } = string.Empty;
    public string FromName { get; set; } = string.Empty;
    public string? BrandLogoUrl { get; set; }

    public int DeliveryAttempts { get; set; } = 3;
    public TimeSpan InitialRetryDelay { get; set; } = TimeSpan.FromMinutes(5);
    public TimeSpan DeliveryTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public List<string> AdminRecipients { get; set; } = new();

    public IReadOnlyList<string> GetValidAdminRecipients()
    {
        return AdminRecipients
            .Where(email => !string.IsNullOrWhiteSpace(email))
            .Select(email => email.Trim())
            .ToList();
    }

    public bool IsReady([NotNullWhen(false)] out string? configurationError)
    {
        if (!Enabled)
        {
            configurationError = null;
            return true;
        }

        if (string.IsNullOrWhiteSpace(Host) || Port <= 0)
        {
            configurationError = "SMTP Host and Port must be configured.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(FromAddress))
        {
            configurationError = "A FromAddress is required to dispatch emails.";
            return false;
        }

        configurationError = null;
        return true;
    }

    public bool TryGetBrandLogoUrl([NotNullWhen(true)] out string? logoUrl)
    {
        if (string.IsNullOrWhiteSpace(BrandLogoUrl))
        {
            logoUrl = null;
            return false;
        }

        logoUrl = BrandLogoUrl.Trim();
        return true;
    }
}

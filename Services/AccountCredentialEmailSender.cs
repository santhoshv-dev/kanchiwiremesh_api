using System.Net;
using System.Net.Mail;
using System.Text.Encodings.Web;
using Microsoft.Extensions.Options;

namespace KanchimeshAPI.Services;

/// <summary>
/// Sends account credential messages before a controller commits a password or
/// account change. A delivery failure therefore never leaves a user with an
/// unknown password or an administrator account that cannot be accessed.
/// </summary>
public interface IAccountCredentialEmailSender
{
    bool IsDeliveryEnabled { get; }
    bool IsReady { get; }

    Task<bool> SendPasswordResetAsync(
        string email,
        string displayName,
        string temporaryPassword,
        CancellationToken cancellationToken);

    Task<bool> SendAdministratorCredentialsAsync(
        string email,
        string displayName,
        string temporaryPassword,
        CancellationToken cancellationToken);
}

public sealed class SmtpAccountCredentialEmailSender : IAccountCredentialEmailSender
{
    private readonly SmtpEmailOptions _options;
    private readonly ILogger<SmtpAccountCredentialEmailSender> _logger;

    public SmtpAccountCredentialEmailSender(
        IOptions<SmtpEmailOptions> options,
        ILogger<SmtpAccountCredentialEmailSender> logger)
    {
        _options = options.Value;
        _logger = logger;
        IsReady = _options.IsReady(out var configurationError);

        if (_options.Enabled && !IsReady)
        {
            SafeLog(() => _logger.LogError(
                "Account credential email delivery is enabled but unavailable: {ConfigurationError}",
                configurationError));
        }
    }

    public bool IsDeliveryEnabled => _options.Enabled;
    public bool IsReady { get; }

    public Task<bool> SendPasswordResetAsync(
        string email,
        string displayName,
        string temporaryPassword,
        CancellationToken cancellationToken) =>
        SendAsync(
            email,
            "Your Temporary Password",
            BuildPasswordResetBody(displayName, temporaryPassword),
            cancellationToken);

    public Task<bool> SendAdministratorCredentialsAsync(
        string email,
        string displayName,
        string temporaryPassword,
        CancellationToken cancellationToken) =>
        SendAsync(
            email,
            "Your Admin Credentials",
            BuildAdministratorCredentialsBody(email, displayName, temporaryPassword),
            cancellationToken);

    private async Task<bool> SendAsync(
        string recipient,
        string subject,
        string htmlBody,
        CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            SafeLog(() => _logger.LogWarning(
                "Account credential email was not sent because SMTP delivery is disabled."));
            return false;
        }

        if (!IsReady)
        {
            SafeLog(() => _logger.LogWarning(
                "Account credential email was not sent because SMTP is not configured correctly."));
            return false;
        }

        var timeout = _options.DeliveryTimeout > TimeSpan.Zero
            ? _options.DeliveryTimeout
            : TimeSpan.FromSeconds(30);
        var timeoutMilliseconds = (int)Math.Min(timeout.TotalMilliseconds, int.MaxValue);

        try
        {
            using var message = new MailMessage
            {
                From = CreateFromAddress(),
                Subject = subject,
                IsBodyHtml = true,
                Body = htmlBody,
            };
            message.To.Add(new MailAddress(recipient));

            using var client = new SmtpClient(_options.Host.Trim(), _options.Port)
            {
                EnableSsl = _options.UseSsl,
                UseDefaultCredentials = false,
                Credentials = new NetworkCredential(_options.Username, _options.Password),
                Timeout = timeoutMilliseconds,
            };

            await client.SendMailAsync(message).WaitAsync(timeout, cancellationToken);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (TimeoutException exception)
        {
            SafeLog(() => _logger.LogWarning(
                exception,
                "Account credential email timed out for {Email}.",
                recipient));
            return false;
        }
        catch (Exception exception)
        {
            SafeLog(() => _logger.LogWarning(
                exception,
                "Account credential email failed for {Email}.",
                recipient));
            return false;
        }
    }

    private MailAddress CreateFromAddress() => string.IsNullOrWhiteSpace(_options.FromName)
        ? new MailAddress(_options.FromAddress.Trim())
        : new MailAddress(_options.FromAddress.Trim(), _options.FromName.Trim());

    private string BuildPasswordResetBody(string displayName, string temporaryPassword)
    {
        var logoHtml = BuildLogoHtml();
        return $"""
            <!doctype html>
            <html lang="en">
              <body style="margin:0;padding:20px;font-family:sans-serif;">
                {logoHtml}
                <p>Hello {Html(displayName)},</p>
                <p>A password reset has been requested for your account.</p>
                <p><strong>Your temporary password:</strong> {Html(temporaryPassword)}</p>
                <p>Please sign in and update your password from the Settings page.</p>
              </body>
            </html>
            """;
    }

    private string BuildAdministratorCredentialsBody(
        string email,
        string displayName,
        string temporaryPassword)
    {
        var logoHtml = BuildLogoHtml();
        return $"""
            <!doctype html>
            <html lang="en">
              <body style="margin:0;padding:20px;font-family:sans-serif;">
                {logoHtml}
                <p>Hello {Html(displayName)},</p>
                <p>An administrator account has been created for you.</p>
                <p><strong>Email:</strong> {Html(email)}<br/>
                <strong>Password:</strong> {Html(temporaryPassword)}</p>
                <p>Please sign in and change your password.</p>
              </body>
            </html>
            """;
    }

    private string BuildLogoHtml() => _options.TryGetBrandLogoUrl(out var logoUrl)
        ? $"<img src=\"{Html(logoUrl)}\" alt=\"Logo\" style=\"display:block;max-width:180px;height:auto;margin-bottom:20px;\" />"
        : string.Empty;

    private static string Html(string value) => HtmlEncoder.Default.Encode(value);

    private static void SafeLog(Action writeLog)
    {
        try
        {
            writeLog();
        }
        catch
        {
            // A broken host log sink must not change account credentials.
        }
    }
}

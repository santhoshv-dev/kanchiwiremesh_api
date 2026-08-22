using System.Net;
using System.Net.Mail;
using System.Text;
using System.Text.Encodings.Web;
using KanchimeshAPI.Models;
using Microsoft.Extensions.Options;

namespace KanchimeshAPI.Services;

public sealed record EmailDispatchResult(string Status, bool IsSent, bool ShouldRetry)
{
    public static EmailDispatchResult Sent() => new(EmailDeliveryStatuses.Sent, true, false);
    public static EmailDispatchResult Disabled() => new(EmailDeliveryStatuses.Disabled, false, false);
    public static EmailDispatchResult PermanentFailure() => new(EmailDeliveryStatuses.Failed, false, false);
    public static EmailDispatchResult TransientFailure() => new(EmailDeliveryStatuses.Failed, false, true);
}

public interface IEnquiryEmailSender
{
    bool IsDeliveryEnabled { get; }
    bool IsReady { get; }
    IReadOnlyList<string> AdminRecipients { get; }

    Task<EmailDispatchResult> SendAsync(
        EmailDeliveryJob job,
        Enquiry enquiry,
        CancellationToken cancellationToken);
}

/// <summary>
/// Sends branded enquiry email jobs that were already committed to the durable
/// outbox. SMTP availability therefore never determines whether a customer
/// enquiry is accepted.
/// </summary>
public sealed class SmtpEnquiryEmailSender : IEnquiryEmailSender
{
    private readonly SmtpEmailOptions _options;
    private readonly ILogger<SmtpEnquiryEmailSender> _logger;

    public SmtpEnquiryEmailSender(
        IOptions<SmtpEmailOptions> options,
        ILogger<SmtpEnquiryEmailSender> logger)
    {
        _options = options.Value;
        _logger = logger;
        AdminRecipients = _options.GetValidAdminRecipients();
        IsReady = _options.IsReady(out var configurationError);

        if (_options.Enabled && !IsReady)
        {
            SafeLog(() => _logger.LogError(
                "SMTP delivery is enabled but unavailable: {ConfigurationError}", configurationError));
        }

        if (_options.Enabled && _options.AdminRecipients.Count != AdminRecipients.Count)
        {
            SafeLog(() => _logger.LogWarning(
                "One or more SMTP administrator recipient addresses are invalid and will be ignored."));
        }
    }

    public bool IsDeliveryEnabled => _options.Enabled;
    public bool IsReady { get; }
    public IReadOnlyList<string> AdminRecipients { get; }

    public async Task<EmailDispatchResult> SendAsync(
        EmailDeliveryJob job,
        Enquiry enquiry,
        CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return EmailDispatchResult.Disabled();
        }

        if (!IsReady)
        {
            return EmailDispatchResult.PermanentFailure();
        }

        MailMessage? message = null;
        try
        {
            message = BuildMessage(job, enquiry);
            using var client = new SmtpClient(_options.Host.Trim(), _options.Port)
            {
                EnableSsl = _options.UseSsl,
                UseDefaultCredentials = false,
                Credentials = new NetworkCredential(_options.Username, _options.Password),
                Timeout = (int)_options.DeliveryTimeout.TotalMilliseconds,
            };

            await client.SendMailAsync(message).WaitAsync(_options.DeliveryTimeout, cancellationToken);
            SafeLog(() => _logger.LogInformation(
                "{EmailKind} email delivered for enquiry {EnquiryNumber}.",
                job.Kind,
                enquiry.EnquiryNumber));
            return EmailDispatchResult.Sent();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Do not turn a controlled application shutdown into a failed job.
            throw;
        }
        catch (TimeoutException exception)
        {
            SafeLog(() => _logger.LogWarning(
                exception,
                "{EmailKind} email timed out for enquiry {EnquiryNumber}.",
                job.Kind,
                enquiry.EnquiryNumber));
            return EmailDispatchResult.TransientFailure();
        }
        catch (FormatException exception)
        {
            SafeLog(() => _logger.LogWarning(
                exception,
                "Email job {EmailJobId} has an invalid sender or recipient address.",
                job.Id));
            return EmailDispatchResult.PermanentFailure();
        }
        catch (SmtpException exception)
        {
            SafeLog(() => _logger.LogWarning(
                exception,
                "{EmailKind} SMTP delivery failed for enquiry {EnquiryNumber}.",
                job.Kind,
                enquiry.EnquiryNumber));
            return EmailDispatchResult.TransientFailure();
        }
        catch (ArgumentOutOfRangeException exception)
        {
            SafeLog(() => _logger.LogWarning(
                exception,
                "Email job {EmailJobId} has unsupported data.",
                job.Id));
            return EmailDispatchResult.PermanentFailure();
        }
        catch (Exception exception)
        {
            SafeLog(() => _logger.LogError(
                exception,
                "{EmailKind} email delivery failed for enquiry {EnquiryNumber}.",
                job.Kind,
                enquiry.EnquiryNumber));
            return EmailDispatchResult.TransientFailure();
        }
        finally
        {
            message?.Dispose();
        }
    }

    private MailMessage BuildMessage(EmailDeliveryJob job, Enquiry enquiry)
    {
        var isCustomerConfirmation = string.Equals(
            job.Kind,
            EmailDeliveryJobKinds.CustomerConfirmation,
            StringComparison.Ordinal);
        var isAdminNotification = string.Equals(
            job.Kind,
            EmailDeliveryJobKinds.AdminNotification,
            StringComparison.Ordinal);
        if (!isCustomerConfirmation && !isAdminNotification)
        {
            throw new ArgumentOutOfRangeException(nameof(job.Kind), job.Kind, "Unsupported enquiry email job kind.");
        }

        var message = new MailMessage
        {
            From = CreateFromAddress(),
            Subject = isCustomerConfirmation
                ? $"We received your enquiry {enquiry.EnquiryNumber}"
                : $"New enquiry {enquiry.EnquiryNumber} from {enquiry.ContactName}",
            SubjectEncoding = Encoding.UTF8,
            Body = isCustomerConfirmation ? BuildCustomerHtml(enquiry) : BuildAdministratorHtml(enquiry),
            BodyEncoding = Encoding.UTF8,
            IsBodyHtml = true,
        };
        message.To.Add(new MailAddress(job.Recipient));
        message.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(
            isCustomerConfirmation ? BuildCustomerPlainText(enquiry) : BuildAdministratorPlainText(enquiry),
            Encoding.UTF8,
            "text/plain"));

        if (isAdminNotification && TryCreateReplyToAddress(enquiry.Email, out var replyToAddress))
        {
            message.ReplyToList.Add(replyToAddress);
        }

        return message;
    }

    private MailAddress CreateFromAddress() => string.IsNullOrWhiteSpace(_options.FromName)
        ? new MailAddress(_options.FromAddress.Trim())
        : new MailAddress(_options.FromAddress.Trim(), _options.FromName.Trim());

    private static bool TryCreateReplyToAddress(string? email, out MailAddress address)
    {
        address = null!;
        if (string.IsNullOrWhiteSpace(email))
        {
            return false;
        }

        try
        {
            address = new MailAddress(email.Trim());
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private string BuildCustomerHtml(Enquiry enquiry)
    {
        var product = string.IsNullOrWhiteSpace(enquiry.ProductRequirement)
            ? "your requirement"
            : enquiry.ProductRequirement;
        return BuildBrandedHtml(
            $"<p>Hello {Html(enquiry.ContactName)},</p>" +
            $"<p>Thank you for contacting Kanchi Wire Mesh. We have received your enquiry for <strong>{Html(product)}</strong>.</p>" +
            ReferenceCard(enquiry.EnquiryNumber) +
            "<p>Our team will review the details and contact you using the information you provided.</p>",
            "Thank you for choosing Kanchi Wire Mesh.");
    }

    private string BuildAdministratorHtml(Enquiry enquiry)
    {
        var rows = new List<(string Label, string? Value)>
        {
            ("Contact", enquiry.ContactName),
            ("Company", enquiry.CompanyName),
            ("Phone", enquiry.Phone),
            ("Email", enquiry.Email),
            ("Requirement", enquiry.ProductRequirement),
            ("Quantity", enquiry.Quantity?.ToString("0.###")),
            ("Unit", enquiry.Unit),
            ("Message", enquiry.Message),
        };
        var tableRows = string.Concat(rows
            .Where(row => !string.IsNullOrWhiteSpace(row.Value))
            .Select(row => $"<tr><th style=\"padding:8px 12px;text-align:left;color:#475569;font-size:13px;border-bottom:1px solid #e2e8f0;vertical-align:top;\">{Html(row.Label)}</th><td style=\"padding:8px 12px;color:#0f172a;font-size:14px;border-bottom:1px solid #e2e8f0;white-space:pre-wrap;\">{HtmlWithBreaks(row.Value)}</td></tr>"));

        return BuildBrandedHtml(
            $"<p>A new public enquiry has been received from <strong>{Html(enquiry.ContactName)}</strong>.</p>" +
            ReferenceCard(enquiry.EnquiryNumber) +
            $"<table role=\"presentation\" cellspacing=\"0\" cellpadding=\"0\" style=\"border-collapse:collapse;width:100%;margin-top:20px;border:1px solid #e2e8f0;border-radius:8px;overflow:hidden;\"><tbody>{tableRows}</tbody></table>",
            "Reply to this email to contact the customer directly.");
    }

    private string BuildBrandedHtml(string content, string footer)
    {
        var header = _options.TryGetBrandLogoUrl(out var logoUrl)
            ? $"<img src=\"{Html(logoUrl)}\" alt=\"Kanchi Wire Mesh\" style=\"display:block;max-width:180px;max-height:72px;width:auto;height:auto;\" />"
            : "<div style=\"font-size:21px;font-weight:800;letter-spacing:.2px;color:#0f172a;\">Kanchi Wire Mesh</div>";

        return $"""
            <!doctype html>
            <html lang="en">
              <body style="margin:0;padding:0;background:#f1f5f9;font-family:Arial,Helvetica,sans-serif;color:#0f172a;">
                <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="background:#f1f5f9;padding:28px 12px;">
                  <tr><td align="center">
                    <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="max-width:620px;background:#ffffff;border-radius:14px;overflow:hidden;box-shadow:0 2px 9px rgba(15,23,42,.08);">
                      <tr><td style="padding:26px 28px 20px;border-bottom:4px solid #0f766e;">{header}</td></tr>
                      <tr><td style="padding:28px;line-height:1.55;font-size:15px;">{content}</td></tr>
                      <tr><td style="padding:18px 28px;background:#f8fafc;color:#64748b;font-size:12px;line-height:1.5;">{footer}</td></tr>
                    </table>
                  </td></tr>
                </table>
              </body>
            </html>
            """;
    }

    private static string ReferenceCard(string enquiryNumber) =>
        $"<div style=\"margin:20px 0;padding:14px 16px;background:#ecfdf5;border-left:4px solid #0f766e;border-radius:6px;\"><span style=\"display:block;color:#475569;font-size:12px;text-transform:uppercase;letter-spacing:.08em;\">Enquiry reference</span><strong style=\"display:block;margin-top:4px;color:#0f172a;font-size:17px;\">{Html(enquiryNumber)}</strong></div>";

    private static string BuildCustomerPlainText(Enquiry enquiry)
    {
        var product = string.IsNullOrWhiteSpace(enquiry.ProductRequirement)
            ? "your requirement"
            : enquiry.ProductRequirement;
        return $"""
            Hello {enquiry.ContactName},

            Thank you for contacting Kanchi Wire Mesh. We have received your enquiry for {product}.

            Reference: {enquiry.EnquiryNumber}

            Our team will review the details and contact you using the information you provided.

            Regards,
            Kanchi Wire Mesh
            """;
    }

    private static string BuildAdministratorPlainText(Enquiry enquiry) => $"""
        New public enquiry received

        Reference: {enquiry.EnquiryNumber}
        Contact: {enquiry.ContactName}
        Company: {enquiry.CompanyName ?? "-"}
        Phone: {enquiry.Phone}
        Email: {enquiry.Email ?? "-"}
        Requirement: {enquiry.ProductRequirement ?? "-"}
        Quantity: {enquiry.Quantity?.ToString("0.###") ?? "-"}
        Unit: {enquiry.Unit ?? "-"}
        Message: {enquiry.Message ?? "-"}
        """;

    private static string Html(string? value) => HtmlEncoder.Default.Encode(value ?? string.Empty);

    private static string HtmlWithBreaks(string? value) => Html(value)
        .Replace("\r\n", "<br />", StringComparison.Ordinal)
        .Replace("\n", "<br />", StringComparison.Ordinal);

    private static void SafeLog(Action writeLog)
    {
        try
        {
            writeLog();
        }
        catch
        {
            // A broken log sink must not prevent a persisted job from retrying.
        }
    }
}

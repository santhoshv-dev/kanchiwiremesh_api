using System.IO;
using System.Net;
using System.Net.Mail;
using System.Net.Mime;
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
    private readonly IHostEnvironment _environment;

    public SmtpEnquiryEmailSender(
        IOptions<SmtpEmailOptions> options,
        ILogger<SmtpEnquiryEmailSender> logger,
        IHostEnvironment environment)
    {
        _options = options.Value;
        _logger = logger;
        _environment = environment;
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
            BodyEncoding = Encoding.UTF8,
            IsBodyHtml = true,
        };
        message.To.Add(new MailAddress(job.Recipient));

        if (isAdminNotification && TryCreateReplyToAddress(enquiry.Email, out var replyToAddress))
        {
            message.ReplyToList.Add(replyToAddress);
        }

        var templateName = isCustomerConfirmation ? "CustomerConfirmation.html" : "AdminNotification.html";
        var templatePath = Path.Combine(_environment.ContentRootPath, "EmailTemplates", templateName);
        var htmlBody = File.ReadAllText(templatePath, Encoding.UTF8);

        htmlBody = htmlBody.Replace("{{ContactName}}", Html(enquiry.ContactName));
        htmlBody = htmlBody.Replace("{{EnquiryNumber}}", Html(enquiry.EnquiryNumber));
        htmlBody = htmlBody.Replace("{{ProductRequirement}}", Html(enquiry.ProductRequirement ?? "your requirement"));
        htmlBody = htmlBody.Replace("{{Year}}", DateTime.Now.Year.ToString());

        if (isAdminNotification)
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
            htmlBody = htmlBody.Replace("{{TableRows}}", tableRows);
        }

        var htmlView = AlternateView.CreateAlternateViewFromString(htmlBody, Encoding.UTF8, MediaTypeNames.Text.Html);

        var logoPath = Path.Combine(_environment.ContentRootPath, "wwwroot", "images", "erp_logo-transparent.png");
        if (File.Exists(logoPath))
        {
            var logoResource = new LinkedResource(logoPath, "image/png")
            {
                ContentId = "brandlogo",
                TransferEncoding = TransferEncoding.Base64
            };
            htmlView.LinkedResources.Add(logoResource);
        }

        message.AlternateViews.Add(htmlView);
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

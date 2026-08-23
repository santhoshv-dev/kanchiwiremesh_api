using KanchimeshAPI.Data;
using KanchimeshAPI.DTOs;
using KanchimeshAPI.Infrastructure;
using KanchimeshAPI.Models;
using KanchimeshAPI.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace KanchimeshAPI.Controllers;

[Route("api/public/enquiries")]
[AllowAnonymous]
[EnableRateLimiting(RateLimitPolicies.PublicEnquiries)]
public sealed class PublicEnquiriesController(
    KanchimeshDbContext database,
    IEnquiryEmailSender emailSender,
    ILogger<PublicEnquiriesController> logger) : ApiControllerBase
{
    [HttpPost]
    [ProducesResponseType(typeof(PublicEnquiryResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<PublicEnquiryResponse>> CreateEnquiry(
        PublicEnquiryRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (!TryNormalizeIdempotencyKey(idempotencyKey, out var submissionKey))
        {
            return ValidationError("Idempotency-Key", "Idempotency-Key must be 1 to 128 printable characters.");
        }

        if (submissionKey is not null)
        {
            var existingEnquiry = await database.Enquiries
                .AsNoTracking()
                .SingleOrDefaultAsync(item => item.PublicSubmissionKey == submissionKey, cancellationToken);
            if (existingEnquiry is not null)
            {
                return Ok(ToPublicResponse(existingEnquiry));
            }
        }

        var email = NullIfWhiteSpace(request.Email);
        var emailDeliveryStatus = email is null
            ? EmailDeliveryStatuses.NotRequested
            : emailSender.IsReady
                ? EmailDeliveryStatuses.Queued
                : emailSender.IsDeliveryEnabled
                    ? EmailDeliveryStatuses.Failed
                    : EmailDeliveryStatuses.Disabled;

        var enquiry = new Enquiry
        {
            EnquiryNumber = DocumentNumbers.New("ENQ"),
            PublicSubmissionKey = submissionKey,
            ContactName = request.ContactName.Trim(),
            CompanyName = NullIfWhiteSpace(request.CompanyName),
            Phone = request.Phone.Trim(),
            Email = email,
            ProductRequirement = NullIfWhiteSpace(request.ProductRequirement),
            Quantity = request.Quantity,
            Unit = NullIfWhiteSpace(request.Unit),
            Message = NullIfWhiteSpace(request.Message),
            Status = "New",
            EmailDeliveryStatus = emailDeliveryStatus,
        };

        database.Enquiries.Add(enquiry);
        database.Notifications.Add(new ApplicationNotification
        {
            Title = "New customer enquiry",
            Message = $"New enquiry {enquiry.EnquiryNumber} received from {enquiry.ContactName}.",
            Type = NotificationTypes.EnquiryReceived,
            RelatedEnquiryId = enquiry.Id,
            RelatedCustomerId = enquiry.CustomerId,
        });

        if (emailSender.IsReady)
        {
            if (enquiry.Email is not null)
            {
                database.EmailDeliveryJobs.Add(new EmailDeliveryJob
                {
                    EnquiryId = enquiry.Id,
                    Kind = EmailDeliveryJobKinds.CustomerConfirmation,
                    Recipient = enquiry.Email,
                });
            }

            foreach (var administratorRecipient in emailSender.AdminRecipients)
            {
                database.EmailDeliveryJobs.Add(new EmailDeliveryJob
                {
                    EnquiryId = enquiry.Id,
                    Kind = EmailDeliveryJobKinds.AdminNotification,
                    Recipient = administratorRecipient,
                });
            }
        }

        try
        {
            // One save commits the enquiry, in-app alert, idempotency key, and
            // all durable email jobs atomically for relational providers. The
            // background worker sends mail only after this transaction succeeds.
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException) when (submissionKey is not null)
        {
            database.ChangeTracker.Clear();
            var concurrentEnquiry = await database.Enquiries
                .AsNoTracking()
                .SingleOrDefaultAsync(item => item.PublicSubmissionKey == submissionKey, cancellationToken);
            if (concurrentEnquiry is not null)
            {
                logger.LogInformation(
                    "Returned the existing enquiry for duplicate public submission key {SubmissionKey}.",
                    submissionKey);
                return Ok(ToPublicResponse(concurrentEnquiry));
            }

            throw;
        }

        return StatusCode(StatusCodes.Status201Created, ToPublicResponse(enquiry));
    }

    private static PublicEnquiryResponse ToPublicResponse(Enquiry enquiry)
    {
        var confirmationEmailSent = string.Equals(
            enquiry.EmailDeliveryStatus,
            EmailDeliveryStatuses.Sent,
            StringComparison.Ordinal);
        var message = confirmationEmailSent
            ? "Your enquiry has been received and a confirmation email has been sent."
            : string.Equals(enquiry.EmailDeliveryStatus, EmailDeliveryStatuses.Queued, StringComparison.Ordinal)
                ? "Your enquiry has been received. We are sending a confirmation email now."
                : "Your enquiry has been received. Our team will follow up using the details you provided.";

        return new PublicEnquiryResponse(
            enquiry.EnquiryNumber,
            enquiry.EmailDeliveryStatus,
            confirmationEmailSent,
            message);
    }

    private static bool TryNormalizeIdempotencyKey(string? value, out string? normalizedKey)
    {
        normalizedKey = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        var candidate = value.Trim();
        if (candidate.Length is 0 or > 128 || candidate.Any(char.IsControl))
        {
            return false;
        }

        normalizedKey = candidate;
        return true;
    }
}

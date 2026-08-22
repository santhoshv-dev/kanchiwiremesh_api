using KanchimeshAPI.Data;
using KanchimeshAPI.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KanchimeshAPI.Services;

/// <summary>
/// Claims and delivers persisted enquiry email jobs. A lease and row-version
/// concurrency check make multiple API instances safe: only the instance that
/// commits the claim performs the send, while an interrupted lease is retried.
/// </summary>
public sealed class EnquiryEmailDeliveryWorker : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan JobLease = TimeSpan.FromMinutes(3);
    private const int BatchSize = 20;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IEnquiryEmailSender _emailSender;
    private readonly SmtpEmailOptions _options;
    private readonly ILogger<EnquiryEmailDeliveryWorker> _logger;

    public EnquiryEmailDeliveryWorker(
        IServiceScopeFactory scopeFactory,
        IEnquiryEmailSender emailSender,
        IOptions<SmtpEmailOptions> options,
        ILogger<EnquiryEmailDeliveryWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _emailSender = emailSender;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await DispatchDueJobsSafelyAsync(stoppingToken);
        using var timer = new PeriodicTimer(PollInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await DispatchDueJobsSafelyAsync(stoppingToken);
        }
    }

    private async Task DispatchDueJobsSafelyAsync(CancellationToken cancellationToken)
    {
        try
        {
            for (var index = 0; index < BatchSize && !cancellationToken.IsCancellationRequested; index++)
            {
                var jobId = await ClaimNextJobAsync(cancellationToken);
                if (!jobId.HasValue)
                {
                    return;
                }

                await DeliverClaimedJobAsync(jobId.Value, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal host shutdown; a leased job becomes available when its lease expires.
        }
        catch (Exception exception)
        {
            SafeLog(() => _logger.LogError(
                exception,
                "The enquiry email delivery worker encountered an unexpected error."));
        }
    }

    private async Task<Guid?> ClaimNextJobAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<KanchimeshDbContext>();
        var now = DateTime.UtcNow;
        var job = await database.EmailDeliveryJobs
            .OrderBy(item => item.NextAttemptAtUtc)
            .ThenBy(item => item.CreatedAtUtc)
            .FirstOrDefaultAsync(item =>
                (item.Status == EmailDeliveryJobStatuses.Pending && item.NextAttemptAtUtc <= now) ||
                (item.Status == EmailDeliveryJobStatuses.Processing &&
                    (!item.LockedUntilUtc.HasValue || item.LockedUntilUtc <= now)),
                cancellationToken);
        if (job is null)
        {
            return null;
        }

        job.Status = EmailDeliveryJobStatuses.Processing;
        job.LockedUntilUtc = now.Add(JobLease);
        try
        {
            await database.SaveChangesAsync(cancellationToken);
            return job.Id;
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another API instance successfully claimed it first.
            return null;
        }
    }

    private async Task DeliverClaimedJobAsync(Guid jobId, CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<KanchimeshDbContext>();
        var job = await database.EmailDeliveryJobs
            .Include(item => item.Enquiry)
            .SingleOrDefaultAsync(item => item.Id == jobId, cancellationToken);
        if (job is null || job.Status != EmailDeliveryJobStatuses.Processing)
        {
            return;
        }

        EmailDispatchResult result;
        try
        {
            result = await _emailSender.SendAsync(job, job.Enquiry, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            SafeLog(() => _logger.LogError(
                exception,
                "Email delivery threw unexpectedly for job {EmailJobId}.",
                job.Id));
            result = EmailDispatchResult.TransientFailure();
        }

        var completedAtUtc = DateTime.UtcNow;
        job.AttemptCount++;
        job.LastAttemptAtUtc = completedAtUtc;
        job.LockedUntilUtc = null;

        if (result.IsSent)
        {
            job.Status = EmailDeliveryJobStatuses.Sent;
            job.SentAtUtc = completedAtUtc;
            job.LastError = null;
        }
        else if (result.ShouldRetry && job.AttemptCount < _options.DeliveryAttempts)
        {
            job.Status = EmailDeliveryJobStatuses.Pending;
            job.NextAttemptAtUtc = completedAtUtc.Add(GetRetryDelay(job.AttemptCount));
            job.LastError = "Email delivery failed and will be retried.";
        }
        else
        {
            job.Status = EmailDeliveryJobStatuses.Failed;
            job.LastError = result.Status == EmailDeliveryStatuses.Disabled
                ? "SMTP delivery is disabled."
                : "Email delivery failed.";
        }

        if (job.Kind == EmailDeliveryJobKinds.CustomerConfirmation)
        {
            job.Enquiry.EmailDeliveryStatus = result.IsSent
                ? EmailDeliveryStatuses.Sent
                : result.ShouldRetry && job.Status == EmailDeliveryJobStatuses.Pending
                    ? EmailDeliveryStatuses.Queued
                    : result.Status;
            job.Enquiry.EmailDeliveryAttemptedAtUtc = completedAtUtc;
        }

        await database.SaveChangesAsync(cancellationToken);
    }

    private TimeSpan GetRetryDelay(int attemptsMade)
    {
        var multiplier = 1 << Math.Min(Math.Max(attemptsMade - 1, 0), 5);
        var seconds = Math.Min(_options.InitialRetryDelay.TotalSeconds * multiplier, 3600d);
        return TimeSpan.FromSeconds(seconds);
    }

    private static void SafeLog(Action writeLog)
    {
        try
        {
            writeLog();
        }
        catch
        {
            // Logging must remain observational: delivery state still needs saving.
        }
    }
}

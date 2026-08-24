using KanchimeshAPI.Data;
using KanchimeshAPI.DTOs;
using KanchimeshAPI.Infrastructure;
using KanchimeshAPI.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KanchimeshAPI.Controllers;

[Route("api/notifications")]
[Authorize(Policy = AuthorizationPolicies.Administrator)]
public sealed class NotificationsController(KanchimeshDbContext database) : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<NotificationDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<NotificationDto>>> GetNotifications(
        [FromQuery] bool unreadOnly = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        (page, pageSize) = NormalizePage(page, pageSize);
        var query = database.Notifications.AsNoTracking().AsQueryable();
        if (unreadOnly)
        {
            query = query.Where(notification => !notification.IsRead);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var notifications = await query
            .OrderByDescending(notification => notification.CreatedAtUtc)
            .ThenByDescending(notification => notification.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(notification => new NotificationDto(
                notification.Id,
                notification.Title,
                notification.Message,
                notification.Type,
                notification.RelatedEnquiryId,
                notification.RelatedCustomerId,
                notification.CreatedAtUtc,
                notification.IsRead,
                notification.ReadAtUtc))
            .ToListAsync(cancellationToken);

        return Ok(new PagedResult<NotificationDto>(notifications, page, pageSize, totalCount));
    }

    [HttpGet("unread-count")]
    [ProducesResponseType(typeof(UnreadNotificationCountDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<UnreadNotificationCountDto>> GetUnreadCount(CancellationToken cancellationToken) =>
        Ok(new UnreadNotificationCountDto(
            await database.Notifications.CountAsync(notification => !notification.IsRead, cancellationToken)));

    [HttpPatch("{id:guid}/read")]
    [ProducesResponseType(typeof(NotificationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<NotificationDto>> MarkAsRead(Guid id, CancellationToken cancellationToken)
    {
        var notification = await database.Notifications
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (notification is null)
        {
            return NotFound();
        }

        if (!notification.IsRead)
        {
            notification.IsRead = true;
            notification.ReadAtUtc = DateTime.UtcNow;
            await database.SaveChangesAsync(cancellationToken);
        }

        return Ok(ToDto(notification));
    }

    [HttpPost("mark-all-read")]
    [ProducesResponseType(typeof(MarkNotificationsReadResultDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<MarkNotificationsReadResultDto>> MarkAllAsRead(CancellationToken cancellationToken)
    {
        var readAtUtc = DateTime.UtcNow;
        var updatedCount = await database.Notifications
            .Where(notification => !notification.IsRead)
            .ExecuteUpdateAsync(updates => updates
                .SetProperty(notification => notification.IsRead, true)
                .SetProperty(notification => notification.ReadAtUtc, readAtUtc), cancellationToken);
        return Ok(new MarkNotificationsReadResultDto(updatedCount));
    }

    private static NotificationDto ToDto(ApplicationNotification notification) => new(
        notification.Id,
        notification.Title,
        notification.Message,
        notification.Type,
        notification.RelatedEnquiryId,
        notification.RelatedCustomerId,
        notification.CreatedAtUtc,
        notification.IsRead,
        notification.ReadAtUtc);
}

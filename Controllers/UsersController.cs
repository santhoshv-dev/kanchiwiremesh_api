using KanchimeshAPI.Data;
using KanchimeshAPI.DTOs;
using KanchimeshAPI.Infrastructure;
using KanchimeshAPI.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using KanchimeshAPI.Services;
using System.ComponentModel.DataAnnotations;

namespace KanchimeshAPI.Controllers;

[Route("api/users")]
[Authorize(Policy = AuthorizationPolicies.Administrator)]
public sealed class UsersController(
    KanchimeshDbContext database,
    IPasswordHasher<ApplicationUser> passwordHasher,
    IAccountCredentialEmailSender accountEmailSender,
    ILogger<UsersController> logger) : ApiControllerBase
{
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult> CreateUser(CreateUserRequest request, CancellationToken cancellationToken)
    {
        var email = request.Email.Trim();
        var displayName = request.DisplayName.Trim();
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return ValidationError(nameof(request.DisplayName), "Display name is required.");
        }

        var normalizedEmail = email.ToUpperInvariant();
        var exists = await database.ApplicationUsers.AnyAsync(u => u.NormalizedEmail == normalizedEmail, cancellationToken);
        if (exists)
        {
            return ValidationError(nameof(request.Email), "A user with this email already exists.");
        }

        var password = GenerateRandomPassword();
        var user = new ApplicationUser
        {
            Email = email,
            NormalizedEmail = normalizedEmail,
            DisplayName = displayName,
            Role = ApplicationRoles.Administrator,
            MustChangePassword = true,
            IsActive = true
        };
        user.PasswordHash = passwordHasher.HashPassword(user, password);

        database.ApplicationUsers.Add(user);
        await database.SaveChangesAsync(cancellationToken);

        // Persist the account before contacting SMTP so a successful email can
        // never refer to an account that failed to commit. If delivery fails,
        // remove the just-created account so the administrator can retry safely.
        var delivered = await accountEmailSender.SendAdministratorCredentialsAsync(
            user.Email,
            user.DisplayName,
            password,
            cancellationToken);
        if (!delivered)
        {
            try
            {
                database.ApplicationUsers.Remove(user);
                await database.SaveChangesAsync(cancellationToken);
            }
            catch (Exception cleanupException)
            {
                SafeLog(() => logger.LogError(
                    cleanupException,
                    "Credential email failed and the temporary administrator account could not be removed for {Email}.",
                    user.Email));
            }

            SafeLog(() => logger.LogWarning(
                "Administrator account creation was rolled back because the credential email failed for {Email}.",
                user.Email));
            return Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Credential email could not be sent.",
                detail: "Check the SMTP configuration and try again.");
        }

        return StatusCode(StatusCodes.Status201Created);
    }

    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult> GetUsers(CancellationToken cancellationToken)
    {
        var users = await database.ApplicationUsers
            .OrderBy(u => u.DisplayName)
            .Select(u => new
            {
                u.Id,
                u.Email,
                u.DisplayName,
                u.Role,
                u.IsActive
            })
            .ToListAsync(cancellationToken);
        return Ok(users);
    }

    private static string GenerateRandomPassword()
    {
        return Guid.NewGuid().ToString("N")[..12] + "Aa1!";
    }

    private static void SafeLog(Action writeLog)
    {
        try
        {
            writeLog();
        }
        catch
        {
            // A broken host log sink must not change account creation.
        }
    }
}

public sealed record CreateUserRequest(
    [param: Required, EmailAddress, StringLength(254)] string Email,
    [param: Required, StringLength(150)] string DisplayName);

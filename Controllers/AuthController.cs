using System.Security.Claims;
using KanchimeshAPI.Data;
using KanchimeshAPI.DTOs;
using KanchimeshAPI.Infrastructure;
using KanchimeshAPI.Models;
using KanchimeshAPI.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace KanchimeshAPI.Controllers;

[Route("api/auth")]
public sealed class AuthController(
    KanchimeshDbContext database,
    IPasswordHasher<ApplicationUser> passwordHasher,
    IJwtTokenService tokenService,
    IAccountCredentialEmailSender accountEmailSender,
    ILogger<AuthController> logger) : ApiControllerBase
{
    [AllowAnonymous]
    [HttpPost("login")]
    [ProducesResponseType(typeof(LoginResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<LoginResponseDto>> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        var normalizedEmail = NormalizeEmail(request.EmailOrPhone);
        var user = await database.ApplicationUsers
            .SingleOrDefaultAsync(item => item.NormalizedEmail == normalizedEmail, cancellationToken);
        if (user is null || !user.IsActive)
        {
            logger.LogWarning("An authentication attempt failed.");
            return InvalidCredentials();
        }

        var passwordResult = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        if (passwordResult == PasswordVerificationResult.Failed)
        {
            logger.LogWarning("An authentication attempt failed.");
            return InvalidCredentials();
        }

        if (passwordResult == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = passwordHasher.HashPassword(user, request.Password);
        }

        // Password changes are voluntary. Clear the historical bootstrap flag
        // when a user successfully signs in so older clients receive the same
        // non-blocking session state as new accounts.
        user.MustChangePassword = false;
        user.LastLoginAtUtc = DateTime.UtcNow;
        await database.SaveChangesAsync(cancellationToken);
        logger.LogInformation("An application user signed in successfully.");
        return Ok(CreateLoginResponse(user));
    }

    [Authorize(Policy = AuthorizationPolicies.PasswordChange)]
    [HttpGet("me")]
    [ProducesResponseType(typeof(AuthenticatedUserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthenticatedUserDto>> GetCurrentUser(CancellationToken cancellationToken)
    {
        var user = await FindCurrentUserAsync(cancellationToken);
        if (user is null || !user.IsActive)
        {
            return Unauthorized();
        }

        return Ok(ToAuthenticatedUserDto(user));
    }

    [Authorize(Policy = AuthorizationPolicies.PasswordChange)]
    [HttpPost("change-password")]
    [ProducesResponseType(typeof(LoginResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<LoginResponseDto>> ChangePassword(
        ChangePasswordRequest request,
        CancellationToken cancellationToken)
    {
        var user = await FindCurrentUserAsync(cancellationToken);
        if (user is null || !user.IsActive)
        {
            return Unauthorized();
        }

        var currentPasswordResult = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.CurrentPassword);
        if (currentPasswordResult == PasswordVerificationResult.Failed)
        {
            logger.LogWarning("An authenticated password-change attempt failed current-password verification.");
            return ValidationError(nameof(request.CurrentPassword), "The current password is incorrect.");
        }

        if (passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.NewPassword) != PasswordVerificationResult.Failed)
        {
            return ValidationError(nameof(request.NewPassword), "The new password must be different from the current password.");
        }

        user.PasswordHash = passwordHasher.HashPassword(user, request.NewPassword);
        user.MustChangePassword = false;
        await database.SaveChangesAsync(cancellationToken);
        logger.LogInformation("An application user changed their password.");
        return Ok(CreateLoginResponse(user));
    }

    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.PasswordResets)]
    [HttpPost("forgot-password")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult> ForgotPassword(ForgotPasswordRequest request, CancellationToken cancellationToken)
    {
        var normalizedEmail = NormalizeEmail(request.Email);
        var user = await database.ApplicationUsers
            .SingleOrDefaultAsync(item => item.NormalizedEmail == normalizedEmail, cancellationToken);
        if (user is null || !user.IsActive)
        {
            // Do not leak if the email exists, just return ok
            return Ok();
        }

        var newPassword = Guid.NewGuid().ToString("N")[..12] + "Aa1!";
        var delivered = await accountEmailSender.SendPasswordResetAsync(
            user.Email,
            user.DisplayName,
            newPassword,
            cancellationToken);
        if (!delivered)
        {
            // Keep the response generic so callers cannot discover whether an
            // account exists, but never invalidate a password without a sent
            // credential email.
            SafeLog(() => logger.LogWarning(
                "Password reset credentials were not changed because email delivery failed for {Email}.",
                user.Email));
            return Ok();
        }

        user.PasswordHash = passwordHasher.HashPassword(user, newPassword);
        user.MustChangePassword = true;
        await database.SaveChangesAsync(cancellationToken);
        SafeLog(() => logger.LogInformation("A password reset was completed for an application user."));
        return Ok();
    }

    private async Task<ApplicationUser?> FindCurrentUserAsync(CancellationToken cancellationToken)
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(userIdClaim, out var userId))
        {
            return null;
        }

        return await database.ApplicationUsers
            .SingleOrDefaultAsync(item => item.Id == userId, cancellationToken);
    }

    private LoginResponseDto CreateLoginResponse(ApplicationUser user)
    {
        var token = tokenService.CreateAccessToken(user);
        return new LoginResponseDto(
            token.AccessToken,
            "Bearer",
            user.DisplayName,
            user.Role,
            user.MustChangePassword,
            token.ExpiresAtUtc);
    }

    private static AuthenticatedUserDto ToAuthenticatedUserDto(ApplicationUser user) => new(
        user.Id,
        user.Email,
        user.DisplayName,
        user.Role,
        user.MustChangePassword);

    private static ActionResult<LoginResponseDto> InvalidCredentials() =>
        new UnauthorizedObjectResult(new ProblemDetails
        {
            Status = StatusCodes.Status401Unauthorized,
            Title = "Invalid sign-in details.",
        });

    private static string NormalizeEmail(string email) => email.Trim().ToUpperInvariant();

    private static void SafeLog(Action writeLog)
    {
        try
        {
            writeLog();
        }
        catch
        {
            // A broken host log sink must not change the endpoint response.
        }
    }
}

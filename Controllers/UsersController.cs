using KanchimeshAPI.Data;
using KanchimeshAPI.DTOs;
using KanchimeshAPI.Infrastructure;
using KanchimeshAPI.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Net.Mail;
using System.Net;
using System.Text.Encodings.Web;
using KanchimeshAPI.Services;

namespace KanchimeshAPI.Controllers;

[Route("api/users")]
[Authorize(Policy = AuthorizationPolicies.Administrator)]
public sealed class UsersController(
    KanchimeshDbContext database,
    IPasswordHasher<ApplicationUser> passwordHasher,
    IOptions<SmtpEmailOptions> emailOptions,
    ILogger<UsersController> logger) : ApiControllerBase
{
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult> CreateUser(CreateUserRequest request, CancellationToken cancellationToken)
    {
        var normalizedEmail = request.Email.Trim().ToUpperInvariant();
        var exists = await database.ApplicationUsers.AnyAsync(u => u.NormalizedEmail == normalizedEmail, cancellationToken);
        if (exists)
        {
            return ValidationError(nameof(request.Email), "A user with this email already exists.");
        }

        var password = GenerateRandomPassword();
        var user = new ApplicationUser
        {
            Email = request.Email.Trim(),
            NormalizedEmail = normalizedEmail,
            DisplayName = request.DisplayName.Trim(),
            Role = ApplicationRoles.Administrator,
            MustChangePassword = true,
            IsActive = true
        };
        user.PasswordHash = passwordHasher.HashPassword(user, password);

        database.ApplicationUsers.Add(user);
        await database.SaveChangesAsync(cancellationToken);

        // Send email with credentials
        var options = emailOptions.Value;
        if (options.Enabled)
        {
            try
            {
                var message = new MailMessage
                {
                    From = string.IsNullOrWhiteSpace(options.FromName) 
                        ? new MailAddress(options.FromAddress.Trim()) 
                        : new MailAddress(options.FromAddress.Trim(), options.FromName.Trim()),
                    Subject = "Your Admin Credentials",
                    IsBodyHtml = true,
                };
                message.To.Add(new MailAddress(user.Email));
                
                var logoHtml = string.Empty;
                if (options.TryGetBrandLogoUrl(out var logoUrl))
                {
                    logoHtml = $"<img src=\"{HtmlEncoder.Default.Encode(logoUrl)}\" alt=\"Logo\" style=\"display:block;max-width:180px;height:auto;margin-bottom:20px;\" />";
                }

                message.Body = $"""
                    <!doctype html>
                    <html lang="en">
                      <body style="margin:0;padding:20px;font-family:sans-serif;">
                        {logoHtml}
                        <p>Hello {HtmlEncoder.Default.Encode(user.DisplayName)},</p>
                        <p>An administrator account has been created for you.</p>
                        <p><strong>Email:</strong> {HtmlEncoder.Default.Encode(user.Email)}<br/>
                        <strong>Password:</strong> {HtmlEncoder.Default.Encode(password)}</p>
                        <p>Please log in and change your password.</p>
                      </body>
                    </html>
                    """;

                using var client = new SmtpClient(options.Host.Trim(), options.Port)
                {
                    EnableSsl = options.UseSsl,
                    UseDefaultCredentials = false,
                    Credentials = new NetworkCredential(options.Username, options.Password),
                };
                await client.SendMailAsync(message, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to send credentials email to {Email}", user.Email);
            }
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
}

public sealed record CreateUserRequest(string Email, string DisplayName);

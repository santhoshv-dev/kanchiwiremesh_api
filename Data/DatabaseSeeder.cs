using KanchimeshAPI.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Net.Mail;

namespace KanchimeshAPI.Data;
public sealed class BootstrapAdministratorOptions
{
    public const string SectionName = "BootstrapAdministrator";

    public string? Email { get; set; }
    public string? DisplayName { get; set; }
    public string? InitialPassword { get; set; }

    public bool TryValidate(out string error)
    {
        if (string.IsNullOrWhiteSpace(Email) || string.IsNullOrWhiteSpace(InitialPassword))
        {
            error = "BootstrapAdministrator:Email and BootstrapAdministrator:InitialPassword must be configured when no administrator exists.";
            return false;
        }

        if (InitialPassword.Length < 12)
        {
            error = "BootstrapAdministrator:InitialPassword must be at least 12 characters.";
            return false;
        }

        try
        {
            _ = new MailAddress(Email.Trim());
        }
        catch (FormatException)
        {
            error = "BootstrapAdministrator:Email must be a valid email address.";
            return false;
        }

        error = string.Empty;
        return true;
    }
}

public static class DatabaseSeeder
{

    /// <returns><c>true</c> when an administrator already exists or was seeded.</returns>
    public static async Task<bool> SeedAsync(
        KanchimeshDbContext context,
        IPasswordHasher<ApplicationUser> passwordHasher,
        BootstrapAdministratorOptions bootstrapAdministrator,
        CancellationToken cancellationToken = default)
    {
        var exists = await context.ApplicationUsers.AsNoTracking().AnyAsync(
            user => user.IsActive && user.Role == ApplicationRoles.Administrator,
            cancellationToken);
        if (exists)
        {
            return true;
        }

        if (!bootstrapAdministrator.TryValidate(out _))
        {
            return false;
        }

        var email = bootstrapAdministrator.Email!.Trim();
        var normalizedEmail = email.ToUpperInvariant();

        var administrator = new ApplicationUser
        {
            Email = email,
            NormalizedEmail = normalizedEmail,
            DisplayName = string.IsNullOrWhiteSpace(bootstrapAdministrator.DisplayName)
                ? "ERH Administrator"
                : bootstrapAdministrator.DisplayName.Trim(),
            Role = ApplicationRoles.Administrator,
            MustChangePassword = false,
            IsActive = true,
        };
        administrator.PasswordHash = passwordHasher.HashPassword(
            administrator,
            bootstrapAdministrator.InitialPassword!);

        context.ApplicationUsers.Add(administrator);
        await context.SaveChangesAsync(cancellationToken);
        return true;
    }
}

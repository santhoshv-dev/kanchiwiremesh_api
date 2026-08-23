using System.Net;
using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using KanchimeshAPI.Data;
using KanchimeshAPI.Infrastructure;
using KanchimeshAPI.Models;
using KanchimeshAPI.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);
static void SafeLog(Action writeLog)
{
    try
    {
        writeLog();
    }
    catch
    {
        // A failing host log sink must not change the API's runtime behavior.
    }
}

var bootstrapAdministrator = builder.Configuration
    .GetSection(BootstrapAdministratorOptions.SectionName)
    .Get<BootstrapAdministratorOptions>() ?? new BootstrapAdministratorOptions();

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services
    .AddControllers()
    .ConfigureApiBehaviorOptions(options =>
    {
        options.InvalidModelStateResponseFactory = ApiValidationResponse.Create;
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHealthChecks();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(RateLimitPolicies.PublicEnquiries, httpContext =>
    {
        var partitionKey = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(10),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0,
                AutoReplenishment = true,
            });
    });
});
builder.Services
    .AddOptions<SmtpEmailOptions>()
    .BindConfiguration(SmtpEmailOptions.SectionName);
builder.Services.AddSingleton<IEnquiryEmailSender, SmtpEnquiryEmailSender>();
builder.Services.AddHostedService<EnquiryEmailDeliveryWorker>();

var jwtOptions = JwtOptions.BindAndValidate(builder.Configuration, builder.Environment);
builder.Services.AddSingleton(jwtOptions);
builder.Services.AddSingleton<IJwtTokenService, JwtTokenService>();
builder.Services.AddScoped<IPasswordHasher<ApplicationUser>, PasswordHasher<ApplicationUser>>();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey)),
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1),
            NameClaimType = ClaimTypes.Name,
            RoleClaimType = ClaimTypes.Role,
        };
    });
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(AuthorizationPolicies.Administrator, policy => policy
        .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme)
        .RequireAuthenticatedUser()
        .RequireRole(ApplicationRoles.Administrator));
    options.AddPolicy(AuthorizationPolicies.PasswordChange, policy => policy
        .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme)
        .RequireAuthenticatedUser());
    options.FallbackPolicy = new AuthorizationPolicyBuilder(JwtBearerDefaults.AuthenticationScheme)
        .RequireAuthenticatedUser()
        .RequireRole(ApplicationRoles.Administrator)
        .Build();
});

var provider = builder.Configuration["Database:Provider"] ?? "SqlServer";
var applyMigrationsOnStartup = builder.Configuration.GetValue<bool>("Database:ApplyMigrationsOnStartup");
if (string.Equals(provider, "InMemory", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddDbContext<KanchimeshDbContext>(options =>
        options.UseInMemoryDatabase("KanchimeshDevelopment"));
}
else
{
    var connectionString = builder.Configuration.GetConnectionString("SqlServer");
    if (string.IsNullOrWhiteSpace(connectionString))
    {
        throw new InvalidOperationException("A SQL Server connection string named 'SqlServer' is required when Database:Provider is SqlServer.");
    }

    builder.Services.AddDbContext<KanchimeshDbContext>(options =>
        options.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure()));
}

const string flutterCorsPolicy = "FlutterClients";
var configuredOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
var configuredOriginSet = new HashSet<string>(configuredOrigins, StringComparer.OrdinalIgnoreCase);

bool IsLoopbackDevelopmentOrigin(string origin)
{
    if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) ||
        (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
    {
        return false;
    }

    return string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase) ||
        (IPAddress.TryParse(uri.Host, out var address) && IPAddress.IsLoopback(address));
}

builder.Services.AddCors(options => options.AddPolicy(flutterCorsPolicy, policy =>
{
    policy.AllowAnyHeader().AllowAnyMethod();
    if (builder.Environment.IsDevelopment())
    {
        if (configuredOrigins.Length > 0)
        {
            // Flutter web selects an ephemeral local port. Preserve explicitly
            // configured origins while allowing only loopback browser origins in
            // Development; production remains restricted to configured origins.
            policy.SetIsOriginAllowed(origin =>
                configuredOriginSet.Contains(origin) || IsLoopbackDevelopmentOrigin(origin));
        }
        else
        {
            // Mobile applications have no browser origin. Allow local browser clients during
            // development as well; production remains limited to configured origins.
            policy.AllowAnyOrigin();
        }
    }
    else if (configuredOrigins.Length > 0)
    {
        policy.WithOrigins(configuredOrigins);
    }
    else
    {
        throw new InvalidOperationException("Configure Cors:AllowedOrigins before running the API outside Development.");
    }
}));

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
if (app.Environment.IsDevelopment())
{
    app.UseSwagger(options => options.RouteTemplate = "openapi/{documentName}.json");
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/openapi/v1.json", "Kanchi Mesh API v1");
    });
}
if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}
app.UseStaticFiles();
app.UseCors(flutterCorsPolicy);
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health").AllowAnonymous();
app.MapControllers();

await using (var scope = app.Services.CreateAsyncScope())
{
    var database = scope.ServiceProvider.GetRequiredService<KanchimeshDbContext>();
    var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<ApplicationUser>>();
    var databaseIsReady = false;
    if (database.Database.IsInMemory())
    {
        await database.Database.EnsureCreatedAsync();
        databaseIsReady = true;
    }
    else if (applyMigrationsOnStartup)
    {
        await database.Database.MigrateAsync();
        databaseIsReady = true;
    }
    else
    {
        try
        {
            databaseIsReady = (await database.Database.GetAppliedMigrationsAsync()).Any();
        }
        catch (Exception exception)
        {
            SafeLog(() => app.Logger.LogWarning(
                exception,
                "Could not verify whether the relational database has an applied migration. Database seeding is deferred."));
        }
    }

    if (databaseIsReady)
    {
        var administratorAvailable = await DatabaseSeeder.SeedAsync(
            database,
            passwordHasher,
            bootstrapAdministrator);
        if (!administratorAvailable)
        {
            bootstrapAdministrator.TryValidate(out var bootstrapError);
            if (!app.Environment.IsDevelopment())
            {
                throw new InvalidOperationException(bootstrapError);
            }

            SafeLog(() => app.Logger.LogWarning(
                "No administrator was seeded. Configure a development bootstrap administrator before signing in. {BootstrapError}",
                bootstrapError));
        }
    }
    else
    {
        SafeLog(() => app.Logger.LogInformation(
            "Database migrations were not applied at startup. The bootstrap administrator will be seeded after the reviewed migrations are applied."));
    }
}

app.Run();

public partial class Program;

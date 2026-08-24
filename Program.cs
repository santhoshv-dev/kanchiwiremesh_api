using System.Globalization;
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
builder.Services
    .AddHealthChecks()
    .AddCheck<DatabaseReadinessHealthCheck>("database");
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(RateLimitPolicies.Login, httpContext =>
    {
        var partitionKey = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(5),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0,
                AutoReplenishment = true,
            });
    });
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
    options.AddPolicy(RateLimitPolicies.PasswordResets, httpContext =>
    {
        var partitionKey = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 3,
                Window = TimeSpan.FromMinutes(15),
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
builder.Services.AddSingleton<IAccountCredentialEmailSender, SmtpAccountCredentialEmailSender>();
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
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = async context =>
            {
                var userIdValue = context.Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                var userVersion = context.Principal?.FindFirst(JwtClaimTypes.UserVersion)?.Value;
                var role = context.Principal?.FindFirst(ClaimTypes.Role)?.Value;
                if (!Guid.TryParse(userIdValue, out var userId) || string.IsNullOrWhiteSpace(userVersion))
                {
                    context.Fail("The access token is missing required user claims.");
                    return;
                }

                var database = context.HttpContext.RequestServices.GetRequiredService<KanchimeshDbContext>();
                var currentUser = await database.ApplicationUsers
                    .AsNoTracking()
                    .Where(user => user.Id == userId)
                    .Select(user => new { user.IsActive, user.Role, user.UpdatedAtUtc })
                    .SingleOrDefaultAsync(context.HttpContext.RequestAborted);
                var currentVersion = currentUser?.UpdatedAtUtc.Ticks.ToString(CultureInfo.InvariantCulture);
                if (currentUser is null ||
                    !currentUser.IsActive ||
                    !string.Equals(currentUser.Role, role, StringComparison.Ordinal) ||
                    !string.Equals(currentVersion, userVersion, StringComparison.Ordinal))
                {
                    context.Fail("The access token is no longer valid.");
                }
            },
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
    var inMemoryDatabaseName = builder.Configuration["Database:InMemoryName"] ?? "KanchimeshDevelopment";
    builder.Services.AddDbContext<KanchimeshDbContext>(options =>
        options.UseInMemoryDatabase(inMemoryDatabaseName));
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
var allowLoopbackCorsOrigins = builder.Configuration.GetValue<bool>("Cors:AllowLoopbackOrigins");

bool IsLoopbackCorsOrigin(string? origin)
{
    if (!IsExactCorsOrigin(origin) ||
        !Uri.TryCreate(origin, UriKind.Absolute, out var uri))
    {
        return false;
    }

    return uri.IsLoopback;
}

bool IsExactCorsOrigin(string? origin)
{
    if (string.IsNullOrWhiteSpace(origin) ||
        !string.Equals(origin, origin.Trim(), StringComparison.Ordinal) ||
        origin.Contains('*') ||
        origin.EndsWith("/", StringComparison.Ordinal) ||
        !Uri.TryCreate(origin, UriKind.Absolute, out var uri) ||
        (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
    {
        return false;
    }

    return !string.IsNullOrEmpty(uri.Host) &&
        string.IsNullOrEmpty(uri.UserInfo) &&
        string.IsNullOrEmpty(uri.Query) &&
        string.IsNullOrEmpty(uri.Fragment) &&
        uri.AbsolutePath == "/";
}

if (configuredOrigins.Any(origin => !IsExactCorsOrigin(origin)))
{
    throw new InvalidOperationException(
        "Cors:AllowedOrigins must contain exact HTTP(S) origins without whitespace, paths, trailing slashes, query strings, fragments, credentials, or wildcards. Configure Cors__AllowedOrigins__0 (and following indexes) on the API host.");
}

builder.Services.AddCors(options => options.AddPolicy(flutterCorsPolicy, policy =>
{
    policy.AllowAnyHeader().AllowAnyMethod();
    if (configuredOrigins.Length > 0 || allowLoopbackCorsOrigins)
    {
        policy.SetIsOriginAllowed(origin =>
            configuredOriginSet.Contains(origin) ||
            (allowLoopbackCorsOrigins && IsLoopbackCorsOrigin(origin)));
    }
    else
    {
        policy.SetIsOriginAllowed(_ => false);
    }
}));

var app = builder.Build();

if (!app.Environment.IsDevelopment() &&
    configuredOrigins.Length == 0 &&
    !allowLoopbackCorsOrigins)
{
    SafeLog(() => app.Logger.LogWarning(
        "No production CORS origins are configured. Cross-origin browser requests will be denied. Set Cors__AllowedOrigins__0 (and following indexes) on the API host, then restart the application."));
}
else if (!app.Environment.IsDevelopment() && allowLoopbackCorsOrigins)
{
    SafeLog(() => app.Logger.LogInformation(
        "CORS accepts loopback browser origins for Flutter Web development."));
}

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

app.MapGet("/", () => Results.Ok(new { status = "running", health = "/health" })).AllowAnonymous();
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
            databaseIsReady = !(await database.Database.GetPendingMigrationsAsync()).Any();
            if (!databaseIsReady)
            {
                SafeLog(() => app.Logger.LogWarning(
                    "Database migrations are pending. Database seeding is deferred."));
            }
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
            "The database is not ready. The bootstrap administrator will be seeded after the reviewed migrations are applied."));
    }
}

app.Run();

public partial class Program;

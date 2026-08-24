# ERH / Kanchi Wire Mesh API

ASP.NET Core 8 API for the ERH Kanchi Wire Mesh public website and Flutter
administrator workspace. It uses EF Core with SQL Server in production and an
in-memory provider only for isolated local development.

## Local development

```powershell
dotnet restore
dotnet user-secrets set "BootstrapAdministrator:Email" "admin@example.test"
dotnet user-secrets set "BootstrapAdministrator:InitialPassword" "use-a-unique-12-character-or-longer-password"
dotnet run --environment Development
```

Development starts at the launch-profile URL, creates an ephemeral in-memory
schema, and never manufactures customers, products, orders, payments, or
enquiries. If no administrator exists, it seeds the account provided through
the development secret store. There is no checked-in default administrator
password, and password changes remain voluntary after sign-in.

Useful development-only diagnostics are Swagger at `/swagger`, OpenAPI at
`/openapi/v1.json`, and the anonymous health endpoint at `/health`.

## Required production configuration

Do not put any production value below in `appsettings.json`. Use a secret
store, platform configuration, or environment variables instead.

| Environment variable | Purpose |
| --- | --- |
| `ConnectionStrings__SqlServer` | SQL Server connection string |
| `Authentication__Jwt__Issuer` | JWT issuer |
| `Authentication__Jwt__Audience` | JWT audience |
| `Authentication__Jwt__SigningKey` | Random secret of at least 32 bytes |
| `BootstrapAdministrator__Email`, `BootstrapAdministrator__InitialPassword` | One-time administrator seed, required only when the user table is empty |
| `BootstrapAdministrator__DisplayName` | Optional display name for that initial administrator |
| `Cors__AllowedOrigins__0` (and following indexes) | Exact public-web / Flutter-web origins |
| `Email__Smtp__Enabled` | Enables server-side customer and admin email delivery |
| `Email__Smtp__Host`, `Port`, `UseSsl` | SMTP transport settings (`smtp.gmail.com`, `587`, and `true` for Gmail STARTTLS) |
| `Email__Smtp__Username`, `Password` | SMTP credentials, kept in a secret store |
| `Email__Smtp__FromAddress`, `FromName` | Sender identity |
| `Email__Smtp__BrandLogoUrl` | Public HTTPS URL for the approved logo, such as `https://www.example.com/erp-logo-transparent.png` |
| `Email__Smtp__AdminRecipients__0` (and following indexes) | Administrator inboxes that receive public-enquiry alerts |

Production fails closed when a SQL Server connection or JWT signing key is
missing. When no CORS origin is configured, same-origin and native clients can
continue to use the API while cross-origin browser requests are denied cleanly.
HTTPS redirection is enabled outside Development. Existing credentials removed
from earlier configuration must be rotated in the external systems where they
were issued.

### Browser CORS deployment

For a separately hosted public website or Flutter web build, add every browser
origin to the API host's production environment settings. An origin is only the
scheme, host, and optional port: do not include a path, trailing slash, API
route, wildcard, or comma-separated list. Use sequential numbered settings for
multiple web applications.

```text
ASPNETCORE_ENVIRONMENT=Production
Cors__AllowedOrigins__0=https://www.example.com
# Optional only when Flutter Web bypasses its development proxy and calls this
# deployed API directly from a dynamic localhost port:
# Cors__AllowLoopbackOrigins=true
```

Replace the example domains with the actual deployed frontend origins. Native
Flutter applications do not send a browser `Origin` header and do not need a
CORS entry. The tracked [production CORS template](appsettings.Production.template.json)
is intentionally secret-free; configure its value in the deployment platform
rather than committing an `appsettings.Production.json`. Restart the API after
changing platform settings because configuration is read during startup.

Verify a deployed public-enquiry form's preflight using its real API and web
origins:

```powershell
curl.exe -i -X OPTIONS "https://api.example.com/api/public/enquiries" -H "Origin: https://www.example.com" -H "Access-Control-Request-Method: POST" -H "Access-Control-Request-Headers: content-type,idempotency-key"
```

The response must include `Access-Control-Allow-Origin` with exactly the
configured web origin. Its absence means the API host did not receive the
matching `Cors__AllowedOrigins__<index>` setting.

`Cors__AllowLoopbackOrigins=true` is deliberately narrower than allowing all
origins: it accepts only `localhost`, `127.0.0.1`, or another loopback address
over HTTP(S), at any port. Enable it only when Flutter Web bypasses its
same-origin development proxy and calls the deployed API directly.

## Google SMTP configuration

SMTP is configured **only on the API host**. Do not put an SMTP username,
password, app password, or administrator email address in Flutter, Vite, or
any browser environment variable. The default non-secret transport values are
already set to Gmail SMTP; delivery stays disabled until all secrets are
provided.

For a Google account, enable 2-Step Verification and create a dedicated app
password for this API. Use the app password rather than the Google account
password. Google documents that app passwords require 2-Step Verification and
may be unavailable to some managed or Advanced Protection accounts; a Google
Workspace administrator can instead configure its approved SMTP relay. See
[Google's app-password guidance](https://support.google.com/mail/answer/185833)
and [Google Workspace SMTP guidance](https://support.google.com/a/answer/176600).

Set these values in the deployment secret store (PowerShell example; replace
the placeholder values):

```powershell
$env:Email__Smtp__Enabled = 'true'
$env:Email__Smtp__Host = 'smtp.gmail.com'
$env:Email__Smtp__Port = '587'
$env:Email__Smtp__UseSsl = 'true'
$env:Email__Smtp__Username = 'notifications@example.com'
$env:Email__Smtp__Password = '<Google app password>'
$env:Email__Smtp__FromAddress = 'notifications@example.com'
$env:Email__Smtp__FromName = 'Kanchi Wire Mesh'
$env:Email__Smtp__BrandLogoUrl = 'https://www.example.com/erp-logo-transparent.png'
$env:Email__Smtp__AdminRecipients__0 = 'sales@example.com'
```

The sender address must be the authenticated Google mailbox or a verified
alias. The logo URL must be publicly reachable by email clients; the supplied
public web project already has `public/erp-logo-transparent.png`, so publish
that file and use its deployed HTTPS URL. Configure the mail secret before
starting the API, then submit a test enquiry and confirm both the customer and
administrator messages arrive.

## Database migration and bootstrap account

The tracked migrations create the application users, customers, products,
enquiries, durable email-delivery jobs, notifications, orders, order items,
and payments tables with their required foreign keys, unique indexes,
concurrency row versions, and audit timestamps. The email-job migration adds a
filtered public idempotency-key index so a browser retry does not create a
second enquiry. Review every generated migration before applying it to a
database that may already contain a legacy schema.

```powershell
dotnet tool restore
$env:ASPNETCORE_ENVIRONMENT = 'Production'
dotnet ef migrations list
dotnet ef database update
```

Set the production connection string and, for an empty database, the bootstrap
administrator secrets in the terminal or deployment secret store before the
final command. The API seeds that administrator once and refuses to start in
production if no administrator exists and the required bootstrap values are
missing. `Database__ApplyMigrationsOnStartup=true` is
available for a tightly controlled one-time deployment, but the recommended
production path is a reviewed migration job. Do not enable automatic migration
on every application instance.

The supplied design-time factory allows migration generation without committing
a connection string. It does not connect to SQL Server while scaffolding.

### Existing production database recovery

The deployed database uses this additive migration history:

```text
20260813151835_InitialProductionSchema
20260822121500_AddEnquiryEmailDeliveryJobsAndIdempotency
20260822143000_DisableForcedPasswordChanges
20260822150000_AddInventoryStockMonitoring
```

Keep that chain in the deployed API assembly. Do not replace it with a new
single `Initial` migration after a database already exists: EF will treat that
new migration as pending and attempt to create tables that already exist. If
`/health` reports `Unhealthy` because of that mismatch, redeploy the API with
the historical migration files restored; do not edit `__EFMigrationsHistory`
manually. Check the pending state first, then apply only a genuinely new,
reviewed additive migration.

## Authentication and authorization

- `POST /api/auth/login` validates a database-backed, PBKDF2-hashed password
  and returns a 60-minute JWT. Each protected request also verifies that the
  account is active and that its user version still matches the token, so
  password, role, and account changes invalidate older tokens.
- `GET /api/auth/me` returns the authenticated profile.
- `POST /api/auth/change-password` remains available for voluntary password
  changes and returns a renewed JWT.
- `POST /api/auth/forgot-password` is anonymous and IP-rate-limited. It keeps
  the existing password unchanged whenever credential email delivery cannot be
  completed, and always returns a generic response to avoid revealing whether
  an account exists.
- Login attempts are IP-rate-limited to ten requests per five minutes and return
  generic credentials errors.
- All administrator APIs use a fallback authorization policy requiring an
  authenticated `Administrator`.
- `/`, `/health`, login, and `POST /api/public/enquiries` are the intended
  anonymous routes. The root endpoint returns a minimal running-status response
  for a quick browser check; `/health` also verifies database readiness.

## Public enquiries, email, and notifications

`POST /api/public/enquiries` validates and stores a public contact submission
as a new enquiry. In the same database save it creates a linked unread admin
notification and, when SMTP is ready, durable customer-confirmation and
administrator-alert jobs. A bounded background worker sends branded HTML email
after the request completes, retries transient failures, and
records delivery state without exposing transport details to the customer.
The endpoint is rate-limited to five submissions per client IP per ten minutes.
Clients should provide a fresh `Idempotency-Key` header for each form submit
and reuse it only when retrying that same submission.

Administrator notification APIs:

- `GET /api/notifications?unreadOnly=&page=&pageSize=`
- `GET /api/notifications/unread-count`
- `PATCH /api/notifications/{id}/read`
- `POST /api/notifications/mark-all-read`

The dashboard returns live aggregate values, recent orders, and a twelve-month
numeric sales trend. List APIs are paginated and business resources require the
administrator bearer token.

## Product inventory and stock monitoring

Products support create, update, and safe delete (a delete marks the catalogue
item inactive so historical order references remain intact). Each product now
exposes `quantityOnHand`, `reorderLevel`, `isLowStock`, and `isOutOfStock`.
Current stock is not editable through a product update: the API records every
change as an immutable stock movement with its resulting balance.

- `GET /api/inventory/summary?search=&lowStockOnly=&page=&pageSize=` returns
  paginated stock levels, with low-stock items sorted first.
- `GET /api/inventory/movements?productId=&page=&pageSize=` returns the
  auditable movement history for all products or one product.
- `POST /api/inventory/products/{productId}/adjustments` accepts
  `quantityChange`, `movementType` (`StockIn`, `StockOut`, or `Adjustment`),
  optional `reason`, `reference`, and `occurredAtUtc`. Stock-out movements use
  a negative quantity; the API rejects any change that would make stock
  negative.

`initialStock` is accepted only while creating a product and creates an
`OpeningBalance` ledger record. `reorderLevel` can be maintained through the
normal product update endpoint.

## Flutter and public web clients

Flutter requires an explicit `--dart-define=API_BASE_URL=https://api.example`
for every build. The React public website must be built with
`VITE_API_BASE_URL=https://api.example`.

Set `Cors__AllowedOrigins__0` to the exact deployed public-web origin (and add
the Flutter web origin at the next index if it differs). Flutter Web calls the
configured API directly, so its deployed origin also needs a CORS entry. Use
`Cors__AllowLoopbackOrigins=true` only for intentional local Flutter Web
debugging. SMTP belongs to the API deployment only; the web site merely submits
its public enquiry to the API and displays the returned queued/sent state.

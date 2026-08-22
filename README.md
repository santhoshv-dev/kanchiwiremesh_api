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

Production fails closed when a SQL Server connection, JWT signing key, or CORS
origin configuration is missing. HTTPS redirection is enabled outside
Development. Existing credentials removed from earlier configuration must be
rotated in the external systems where they were issued.

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
dotnet ef migrations list -- --environment Production
dotnet ef database update -- --environment Production
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

## Authentication and authorization

- `POST /api/auth/login` validates a database-backed, PBKDF2-hashed password
  and returns a short-lived JWT.
- `GET /api/auth/me` returns the authenticated profile.
- `POST /api/auth/change-password` remains available for voluntary password
  changes and returns a renewed JWT.
- All administrator APIs use a fallback authorization policy requiring an
  authenticated `Administrator`.
- `/health`, login, and `POST /api/public/enquiries` are the only intended
  anonymous routes.

## Public enquiries, email, and notifications

`POST /api/public/enquiries` validates and stores a public contact submission
as a new enquiry. In the same database save it creates a linked unread admin
notification and, when SMTP is ready, durable customer-confirmation and
administrator-alert jobs. A bounded background worker sends branded HTML and
plain-text email after the request completes, retries transient failures, and
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

Flutter must be built with `--dart-define=API_BASE_URL=https://api.example`.
The React public website is built with `VITE_API_BASE_URL=https://api.example`.
Neither client contains a localhost or production fallback URL; each fails with
a clear configuration message until a real base URL is supplied.

Set `Cors__AllowedOrigins__0` to the exact deployed public-web origin (and add
the Flutter web origin at the next index if it differs). SMTP belongs to the
API deployment only; the web site merely submits its public enquiry to the API
and displays the returned queued/sent confirmation state.

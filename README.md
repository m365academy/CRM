# M365 Academy CRM — crm.m365academy.cloud

The sales CRM for M365 Academy: leads, contacts, organisations, a deals
pipeline, tasks, quotes and sales reports, built for selling Microsoft 365
and Azure training. Same stack, conventions and look as
[m365academy.cloud](https://m365academy.cloud) (repository
`m365academy/m365academy-website`), on **.NET 10 (LTS)**.

Website contact-form enquiries arrive here automatically as leads. The
website still emails every enquiry exactly as before; the CRM is in addition
to that, never instead of it.

## What it does

| Area | Route | What's there |
|---|---|---|
| Dashboard | `/` | Open pipeline, weighted forecast, won this month/year, new and unassigned leads, my overdue tasks; my tasks for the week, new leads ("Take it"), pipeline by stage, deals closing in 30 days, recent activity |
| Leads | `/leads` | Website enquiries plus manual and imported leads. Filter by status/source/rating/owner, search, CSV export and import. Quick actions: assign to me, contacted, qualified, unqualified (with reason). Duplicate warning when the email already belongs to a contact or an earlier lead. **Convert** creates or links an organisation, contact and (optionally) a deal priced from the catalogue, and carries the lead's history across |
| Contacts | `/contacts` | People (buyers, training managers, delegates) with marketing-consent flag, deals, quotes, earlier enquiries and a full timeline. CSV import (dedupes on email) and export |
| Organisations | `/organisations` | Prospect / Customer / Partner / Former, sector, headcount, address (for quotes and onsite delivery), won-to-date and open pipeline, contacts, deals, quotes, timeline. A first won deal turns a Prospect into a Customer automatically |
| Pipeline | `/deals` | Drag-and-drop board: New → Qualified → Proposal sent → Negotiation → Won / Lost (lost asks for a reason). Column totals and weighted totals. List view at `/deals/list` with filters and CSV export |
| Deals | `/deals/{id}` | Course, delivery format (public scheduled, private cohort live-online/onsite, bespoke), delegates, value, probability (defaults by stage), expected close, preferred dates, PO number, source, lost reason. Value at £0 = price it from the catalogue |
| Tasks | `/tasks` | Overdue / today / upcoming for me or anyone; complete and reopen. Tasks and activities (call, email, meeting, note) can be logged from every record |
| Email | on every lead, contact, organisation and deal | Send an email from the shared mailbox via Microsoft Graph, with replies going to the salesperson, logged on the timeline automatically |
| Quotes | `/quotes` | Numbered `Q-yyyy-nnnn`, created from a deal with its course line pre-filled. Line editor with catalogue lookup, discount, VAT, terms. Print/PDF view, email to the contact, duplicate. **Accepting a quote wins its deal** at the quote's ex-VAT total; emailing one moves the deal to Proposal sent |
| Reports | `/reports` | For any date range and owner: won revenue, win rate, average deal size, sales cycle, lead conversion; won revenue by month; pipeline by stage; forecast by expected-close month (overdue → +6 months); lead sources with conversion and won revenue; lead outcomes; courses (won revenue, delegates, open pipeline); revenue by category and delivery format; sales team performance; lost reasons; top customers; activity volume; won deals. Every table exports to CSV |
| Search | `/search` (box in the header) | Across leads, contacts, organisations, deals and quotes. A single exact hit, such as a quote number, jumps straight to the record |
| Admin | `/admin/courses`, `/admin/users` | Course catalogue and list prices (seeded from the website's courses with prices at £0 — set them before relying on catalogue pricing). Users: deactivate a leaver, and hand all their open leads, deals and tasks to someone else in one go |

UK throughout: £, en-GB dates, Europe/London times, 20% VAT by default.

## Architecture

One ASP.NET Core 10 MVC application, `server/M365AcademyCrm/`, laid out like
the website:

```
server/M365AcademyCrm/
  Controllers/                     One controller per area (Leads, Contacts, Organisations, Deals,
                                   Activities [tasks + email], Quotes, Reports, Search, Dashboard)
  Controllers/LeadsApiController   POST /api/leads - website contact-form intake (API key)
  Areas/Admin/                     /admin/courses, /admin/users (CRMADMIN only)
  Authentication/                  Entra sign-in hook (CRM group check), dev-only auth bypass
  Data/CrmDbContext.cs             EF Core: SalesUser, Lead, Organisation, Contact, Course, Deal,
                                   Activity, Quote, QuoteLine (+ seed catalogue)
  Migrations/                      EF Core migrations (SQL Server)
  Models/                          Entities, options, view models
  Services/                        GraphGroupChecker, GraphMailSender, LeadIntakeService,
                                   ReportBuilder, Csv, Lookups, Ui (formatting/labels), ...
  Views/                           Razor views; Shared/_Layout.cshtml is the site header in CRM form
  wwwroot/css/styles.css           The m365academy.cloud design system, copied verbatim
  wwwroot/css/crm.css              CRM components (board, tables, charts, quotes) on the same tokens
  wwwroot/js/crm.js                Board drag-and-drop, quote line editor, small form helpers.
                                   Plain DOM, no dependencies, no build step
```

- **No front-end dependencies.** Like the website: plain HTML/CSS/JS. Charts
  are CSS bars, each paired with a data table (the accessible view, and what
  CSV export produces).
- **Every POST is antiforgery-protected** (global
  `AutoValidateAntiforgeryTokenAttribute`); `/api/leads` opts out and uses
  its API key instead.
- **Everything requires a CRM role** via the authorization fallback policy;
  only `/api/leads`, `/api/health`, the error pages and access-denied are
  anonymous.
- **Reports aggregate in memory** from one load of the period's records. A
  training company's CRM holds thousands of rows, not millions, and it keeps
  the arithmetic in one readable place (`Services/ReportBuilder.cs`).

## Website → CRM lead intake

`POST /api/leads` with header `X-Api-Key: <Crm:LeadApiKey>` and JSON
`{ name, email, phone?, company?, jobTitle?, courseOfInterest?, message?, source? }`
(limits mirror the website's `ContactRequest`). Behaviour:

- `courseOfInterest` codes (`AZ-104`, `MCT-Onboarding`, ...) link to the catalogue.
- If the email already has an **open** lead, the enquiry is added to that
  lead's timeline (`merged: true`) instead of creating a duplicate.
- Otherwise a new lead is created with status New, assigned to
  `Crm:DefaultLeadOwnerEmail` if set, else unassigned. It shows on the
  dashboard's "New leads" list and is flagged "Not yet contacted" after 48 hours.
- Constant-time key comparison; an empty `Crm:LeadApiKey` rejects everything (503).
- Rate-limited per IP (30/min) as a backstop.

The website side is `Services/CrmLeadSender.cs` in `m365academy-website`.

## Access control: Entra ID groups

Staff sign in with their M365 Academy work account (single tenant). On each
sign-in the CRM checks two Entra security groups via Microsoft Graph, the
same live-group-check approach the website uses for LMSADMIN:

- **CRM users** (`Crm:UserGroupId`): can use the CRM.
- **CRM admins** (`Crm:AdminGroupId`): can also manage the course catalogue
  and users. Admins don't need to be in the users group too.

Anyone else who signs in gets `/account/access-denied`. Group changes apply
at next sign-in. Deactivating a user in `/admin/users` denies them even
while they're still in the group, and keeps their history.

### One-time Entra ID setup

1. **Create two security groups**, e.g. `CRMUSERS` and `CRMADMIN`, add the
   sales team and admins, and note each group's **Object ID**.
2. **Register an app**: Entra admin center → *App registrations* → *New
   registration*, e.g. "M365 Academy CRM". Supported account types:
   **Accounts in this organizational directory only**.
3. **Redirect URI**: *Authentication* → **Web** →
   `https://crm.m365academy.cloud/signin-oidc` (plus
   `https://localhost:7060/signin-oidc` for local testing against Entra).
4. **API permissions** → Microsoft Graph → **Application permissions** →
   `GroupMember.Read.All` (group check) and `Mail.Send` (CRM emails and
   quotes) → **Grant admin consent**.
5. **(Strongly recommended) restrict `Mail.Send` to the one mailbox**, as
   the website does:

   ```powershell
   New-DistributionGroup -Name "M365AcademyCrmMailSenders" -Type Security -Members hello@m365academy.cloud
   New-ApplicationAccessPolicy -AppId <crm-client-id> -PolicyScopeGroupId M365AcademyCrmMailSenders@m365academy.cloud -AccessRight RestrictAccess -Description "Only allow M365 Academy CRM to send as hello@m365academy.cloud"
   ```
6. **Client secret**: *Certificates & secrets* → *New client secret*.

### App settings

**Do not commit secrets.** Set them via user-secrets locally or environment
variables / a secrets store in production:

```bash
# Production (environment variables, e.g. IIS / App Service configuration)
AzureAd__TenantId=<your-tenant-id>
AzureAd__ClientId=<crm-app-client-id>
AzureAd__ClientSecret=<crm-app-client-secret>
Crm__UserGroupId=<CRMUSERS-object-id>
Crm__AdminGroupId=<CRMADMIN-object-id>
Crm__LeadApiKey=<long-random-string, same as the website's Crm__ApiKey>
ConnectionStrings__CrmDb=<sql-connection-string>
```

Generate the lead API key with something like
`[Convert]::ToBase64String((1..48 | % { Get-Random -Max 256 }))` or
`openssl rand -base64 48`.

Non-secret settings in `appsettings.json`:

- `Crm:MailFromAddress` / `MailFromName`: the mailbox CRM emails come from
  (`hello@m365academy.cloud`). Every CRM email is BCCed there and saved to
  Sent Items, so there's a copy outside the CRM.
- `Crm:DefaultLeadOwnerEmail`: optional; auto-assign website leads to this
  salesperson.
- `Company:*`: what's printed on quotes (address, VAT number, company
  number, default VAT rate, quote validity days, default terms). **Fill in
  `VatNumber` and `CompanyNumber`** if you have them.

### Database

EF Core on SQL Server, a separate database from the website's LMS.
Migrations run automatically at startup, guarded like the website's: left at
`CHANGE_ME` it logs a warning instead of crashing.

```sql
CREATE DATABASE M365AcademyCrm;
GO
USE M365AcademyCrm;
GO
CREATE LOGIN m365academy_crm_app
    WITH PASSWORD = 'REPLACE_WITH_A_STRONG_GENERATED_PASSWORD',
    CHECK_POLICY = ON;
GO
CREATE USER m365academy_crm_app FOR LOGIN m365academy_crm_app;
GO
ALTER ROLE db_datareader ADD MEMBER m365academy_crm_app;
ALTER ROLE db_datawriter ADD MEMBER m365academy_crm_app;
ALTER ROLE db_ddladmin   ADD MEMBER m365academy_crm_app;  -- only because of migrate-on-startup, scoped to this DB only
GO
```

```
Server=<your-sql-server>;Database=M365AcademyCrm;User Id=m365academy_crm_app;Password=<password>;TrustServerCertificate=True;Encrypt=True;
```

New migration after a model change:

```bash
cd server/M365AcademyCrm
dotnet ef migrations add <Name>
```

## Running locally

Needs the .NET 10 SDK. No Entra app or SQL Server is needed:
`appsettings.Development.json` turns on **DevAuth** (every request is a
local admin, "Dev User") and a throwaway **SQLite** file (`crm-dev.db`,
created from the model on startup). Both are refused outside the
Development environment.

```bash
cd server/M365AcademyCrm
dotnet run
# http://localhost:5191
```

Send it a test lead the way the website does:

```bash
curl -X POST http://localhost:5191/api/leads -H "Content-Type: application/json" \
  -H "X-Api-Key: dev-lead-api-key" \
  -d '{"name":"Jo Bloggs","email":"jo@contoso.co.uk","courseOfInterest":"AZ-104","message":"6 admins need AZ-104 in Q1"}'
```

To test real Entra sign-in locally, set `Crm:DevAuth` to `false` and
`Database:Provider` to `SqlServer` in user-secrets, along with the settings above.

## Deploying to crm.m365academy.cloud

Same as the website: any host that runs ASP.NET Core 10 (IIS with the .NET 10
Hosting Bundle, Azure App Service, a container).

```bash
cd server/M365AcademyCrm
dotnet publish -c Release -o ./publish
```

Checklist:

- [ ] DNS `crm.m365academy.cloud` and a TLS certificate.
- [ ] SQL database and login (script above); `ConnectionStrings__CrmDb` set.
- [ ] Entra app, groups and settings (above).
- [ ] The same random key in the CRM's `Crm__LeadApiKey` and the website's `Crm__ApiKey`.
- [ ] Set course prices at `/admin/courses`.
- [ ] Fill in `Company:VatNumber` / `CompanyNumber` for quotes.
- [ ] **Data Protection keys** persist to `%ProgramData%\M365AcademyCrm\dataprotection-keys`;
      check the app pool identity can write there.
- [ ] If IIS/ARR terminates TLS so the app sees plain HTTP, sign-in fails with
      `AADSTS50011` (http redirect URI). Add `app.UseForwardedHeaders(...)`
      trusting only the proxy, same note as the website.

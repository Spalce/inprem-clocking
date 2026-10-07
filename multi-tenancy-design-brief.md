# Multi-Tenancy Design Brief: InpremClockingApp

## Stack

- **Language / framework:** C# on .NET 8. ASP.NET Core 8 with Razor Pages (server-rendered), plus a few Web API controllers called by jQuery on the pages. There's no SPA and no front-end build step; Tailwind CSS is precompiled.
- **Database:** Microsoft SQL Server, accessed through Entity Framework Core 8 (code-first migrations). There is one shared database today.
- **Authentication:** ASP.NET Core Identity (cookie auth), with its user and role tables in the same database as the app data.
- **Other:** QuestPDF generates the hours reports as PDFs.
- **Hosting:** SmarterASP.NET shared Windows/IIS hosting, deployed with Web Deploy. The database is a shared SQL Server instance (`site4now.net`). This limits the options:
  - we have database-owner rights on our one database, not server-level access;
  - extra databases cost extra;
  - wildcard subdomains / custom domains per tenant would need checking with the host;
  - there's no container or background-infrastructure platform.

## Scale

- Typical size per organization: roughly tens to a few hundred staff and volunteers, each clocking in once a day.
- That means low write volume, with reports and PDF exports as the heaviest reads.

## What the app does (domain)

- Community organizations track hours for **staff** and **volunteers**. These are two parallel domains, each with its own people table and clocking table.
- **Kiosk:** a shared device where people register or search for themselves, then clock in, start and end a break, and clock out. There's one clocking session per person per calendar day.
- **Back office:** organization admins manage people, correct clocking records, and run hours reports and PDF exports by date range.
- Working hours = (clock out − clock in) − (break end − break start).
- Timestamps are stored in UTC. The "calendar day" and all displayed times use the organization's local time zone, so each tenant needs its own time zone.

## Constraints the design must meet

1. **No data loss in production.** One organization is live on a single-organization schema with real staff, volunteer, clocking and admin-login records. They must migrate into the first tenant without losing data or credentials.
2. **Uniqueness is per organization, not global.** This covers person emails, admin logins (the same email may exist in two organizations), and the one-session-per-day rule.
3. **The kiosk runs on a shared device.** How the kiosk knows which tenant it belongs to has to be part of the tenant-resolution design.
4. **SQL Server row-level security** would be implemented with `SESSION_CONTEXT` set per connection or request from EF Core. Please check whether creating security policies is possible with database-owner rights on shared hosting, and give a fallback using EF Core global query filters if it isn't.

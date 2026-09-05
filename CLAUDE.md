# FamilyBudget — Project Context for Claude Code

## Purpose

Learning project: deepen C# / ASP.NET Core / EF Core / PostgreSQL skills through building a real, deployed web app for tracking family expenses. The learning goal matters as much as the working product — the developer (owner of this repo) wants to understand and write the core business logic herself; Claude Code is used for scaffolding, mechanical/repetitive work, debugging assistance, and validation (running builds/tests), not for writing the conceptual/business-logic code end-to-end on her behalf unless explicitly asked.

Full specification, phased plan, and DB schema are in:
@TZ_semeynyi_budget.md
@PLAN_semeynyi_budget.md
@DB_SCHEMA_semeynyi_budget.md

## Tech Stack

- **Backend**: ASP.NET Core (.NET 10)
- **Frontend**: Blazor Server (`--interactivity Server`), chosen deliberately to focus on backend skills rather than learning a separate JS framework
- **ORM**: Entity Framework Core 10, provider `Npgsql.EntityFrameworkCore.PostgreSQL`
- **Database**: PostgreSQL — local via Docker for development, Neon (serverless Postgres, Frankfurt region) for production
- **Auth**: ASP.NET Core Identity (`ApplicationUser : IdentityUser<int>`), email/password first, Google OAuth planned as a later addition
- **Hosting**: Render (Docker-based deploy; Render has no native .NET runtime), Frankfurt region
- **CI**: GitHub Actions — build on push/PR to `main`
- **Testing**: xUnit + Testcontainers (planned, not yet implemented)

## Key Architecture Decisions (do not silently deviate from these)

1. **Multi-tenancy via FamilyGroup**: every user belongs to exactly one `FamilyGroup` (a solo user gets an auto-created "group of one" — there is no separate "no group" mode). `Category`, `Product`, `Store`, `Purchase` are all scoped by `FamilyGroupId` — data is isolated per family, not shared globally across the app.
2. **Price belongs to `Purchase`, not `Product`**: `Product` is identified by name only (+ optional category). Price changes over time are expected and must not create duplicate products.
3. **Category hierarchy is self-referencing with unlimited depth** (`ParentCategoryId` nullable FK to `Category`). Cycle prevention (A → B → A) is NOT enforced by a DB constraint — it must be validated in application code before saving.
4. **No "abstract buyers"**: a `Purchase.UserId` must reference a real `ApplicationUser` who is a member of the same `FamilyGroup`. Default is the user who entered the record. This is also not enforced by a plain FK — must be validated in code.
5. **Identity integration**: use `AddIdentity<ApplicationUser, IdentityRole<int>>()...AddDefaultTokenProviders()`, NOT `AddDefaultIdentity` (the latter bundles its own default UI which conflicts with our scaffolded `Areas/Identity/Pages/Account` pages, and doesn't let us specify `IdentityRole<int>` explicitly). `RequireConfirmedAccount` must stay `false` — no email sender is configured.
6. **Login/Register/Logout are classic Razor Pages** (`Areas/Identity/Pages/Account`, scaffolded via `dotnet aspnet-codegenerator identity`), NOT interactive Blazor components. Reason: Identity sign-in sets an auth cookie via the HTTP response; an already-live interactive Blazor Server circuit can't set response headers/cookies because they're already sent. The rest of the app stays Interactive Server as originally planned.
7. **`DbContext` registration uses `AddDbContextFactory`, not `AddDbContext`** — required for Blazor Server to avoid a long-lived `DbContext` per user circuit accumulating tracked entities over hours (a documented real-world memory-growth issue). Any new Blazor component that needs the DB should inject `IDbContextFactory<FamilyBudgetDbContext>` and do `using var context = DbFactory.CreateDbContext();` per operation — never inject `FamilyBudgetDbContext` directly in a Blazor component.
8. **`FamilyBudgetDbContextFactory`** (`Data/FamilyBudgetDbContextFactory.cs`) is a separate `IDesignTimeDbContextFactory<FamilyBudgetDbContext>` used only by `dotnet ef` CLI tooling at design time — it is not part of the app's runtime DI. It reads `appsettings.Development.json` plus environment variables (env vars override the file), which is how migrations get pointed at Neon instead of local Postgres (see Neon section below).
9. **Currency**: MVP uses EUR only, but every `Purchase` has an explicit `Currency` column (not hardcoded) to leave room for future multi-currency support.
10. **All money fields are `decimal`, never `float`/`double`.** Dates on `Purchase` use `DateOnly`, not `DateTime`.
11. **Email sender registration must use fully-qualified type names.** `Areas/Identity/Pages/Account/Register.cshtml.cs` requires `Microsoft.AspNetCore.Identity.UI.Services.IEmailSender` in DI (needed even with `RequireConfirmedAccount = false`, since it's a constructor dependency regardless). There is a second, unrelated generic `IEmailSender<TUser>` elsewhere in the `Microsoft.AspNetCore.Identity` namespace (already `using`'d in `Program.cs` for `AddIdentity`/`IdentityRole`), which causes the compiler to pick the wrong one if you register `IEmailSender` as a bare name. Register it fully qualified instead: `builder.Services.AddSingleton<Microsoft.AspNetCore.Identity.UI.Services.IEmailSender, Microsoft.AspNetCore.Identity.UI.Services.NoOpEmailSender>();` — note `NoOpEmailSender` is a ready-made do-nothing implementation Microsoft already ships in that namespace; no need to hand-write one.

## Neon connection strings — direct vs pooled

- Neon gives connection strings in `postgresql://` URI format; Npgsql needs the key-value format: `Host=...;Port=5432;Database=...;Username=...;Password=...;SSL Mode=Require;Channel Binding=Require`.
- **Direct** (no `-pooler` in hostname) — used for running migrations (`dotnet ef database update`).
- **Pooled** (`-pooler` in hostname) — used for the deployed app's runtime connection string on Render; additionally needs `No Reset On Close=true` (Neon's PgBouncer runs in transaction pooling mode).
- To apply a migration to Neon: set `ConnectionStrings__DefaultConnection` as a session-only environment variable in the terminal (`$env:...` in PowerShell) before running `dotnet ef database update`, then close that terminal window afterward so it doesn't leak into normal local runs. Local development always uses the Docker Postgres connection string in `appsettings.Development.json`.

## Commands

```bash
# Run locally (HTTPS)
dotnet run --launch-profile https

# Local Postgres (Docker)
docker run --name familybudget-db -e POSTGRES_PASSWORD=devpassword -e POSTGRES_DB=familybudget -p 5432:5432 -d postgres:18
docker ps                       # confirm it's actually running, not just installed
docker exec -it familybudget-db psql -U postgres -d familybudget -c "\dt"

# EF Core migrations
dotnet ef migrations add <Name>
dotnet ef migrations remove --force   # if already applied
dotnet ef database update             # applies to whatever DefaultConnection currently resolves to
```

See `NOTES.md` in the repo root for the fuller, evolving list of utility commands and gotchas (Render env var setup, git branch workflow, etc.) — check it before assuming a command or workflow detail.

## Git workflow

- Every piece of work happens on a feature branch (`git checkout -b feature/...`), never directly on `main`.
- CI (`.github/workflows/ci.yml`) runs `dotnet restore && dotnet build` on push and pull_request to `main`.
- Render auto-deploys only from `main`, so `main` must always stay in a working/deployable state.
- After merge: `git checkout main && git pull`, then branch again from there.

## Current status (update this section only after verifying the actual file content — see note below)

Completed: Этап 0 (tooling), Этап 1 (Hello World skeleton + CI/CD + Render deploy), Этап 2 (DB schema design), Этап 3 (EF Core models/DbContext/first migration), Этап 4 (Neon connected to Render).

In progress — Этап 5 (Authentication + family groups): `ApplicationUser`/`IdentityDbContext` migrated and applied (local + Neon). Identity scaffolded via `dotnet aspnet-codegenerator identity` (Login/Register/Logout only, as Razor Pages under `Areas/Identity/Pages/Account`).

`Program.cs` has been **verified fixed** (read directly, not assumed): `using FamilyBudget.Models;` added, `AddDefaultIdentity` replaced with `AddIdentity<ApplicationUser, IdentityRole<int>>(...).AddEntityFrameworkStores<FamilyBudgetDbContext>().AddDefaultTokenProviders()` with `RequireConfirmedAccount = false`, `AddRazorPages()`/`MapRazorPages()` added, `UseAuthentication()`/`UseAuthorization()` added in the correct order, `AddCascadingAuthenticationState()` added, and the `IEmailSender` registration from Architecture Decision #11 added.

**Layout fix verified done**: `_Layout.cshtml`, `_LoginPartial.cshtml`, and `_ValidationScriptsPartial.cshtml` moved from the root `Pages/Shared/` into `Areas/Identity/Pages/Shared/`; `Areas/Identity/Pages/_ViewStart.cshtml` now says `Layout = "_Layout";`; the orphaned root `Pages/` folder was deleted entirely; the `_LoginPartial.cshtml` link to the never-scaffolded `/Account/Manage/Index` was replaced with a plain (non-link) greeting. `dotnet build` succeeds with 0 errors (only the pre-existing low-severity `NU1901` NuGet advisory warnings, unrelated to this work).

**Confirmed by directly opening in the browser**: `/Identity/Account/Login` and `/Identity/Account/Register` both render correctly (unstyled — see cosmetic issue below — but functional, no 404/500).

Known cosmetic issue (not blocking): the layout references `~/Identity/lib/bootstrap/...` and other static asset paths that don't exist in this project's `wwwroot` — forms render unstyled. Deferred to later.

**Not yet done, and intentionally not delegated to Claude Code**: no code anywhere creates a `FamilyGroup` when a user registers. `ApplicationUser.FamilyGroupId` is a non-nullable FK with no default, so as written, every registration attempt fails with a foreign-key violation (confirmed: this is the actual, current failure mode when submitting the Register form, not a hypothetical). Per Architecture Decision #1 / spec 2.2, a "personal group of one" must be created automatically at registration. This is core business logic and is being written by the developer herself, not scaffolded or generated — do not implement this without being explicitly asked to.

Not yet verified end-to-end (register → login → logout flow still blocked on the `FamilyGroup`-at-registration gap above). Family group creation/joining screens not started. Google OAuth not started.

**Lesson learned the hard way**: earlier versions of this file described several of the above fixes as "done" based on what was discussed in a mentoring conversation, before the actual file changes were confirmed saved. They turned out not to have been applied at all. Do not update this section based on what was *discussed* or *planned* — only after directly reading the file and confirming the change is actually present on disk.

Not started: Этап 6 (purchase tracking UI), Этап 7 (reports), Этап 8 (end-to-end deployed run-through), Этап 9 (Cowork CSV import), Этап 10 (tests).

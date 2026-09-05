# FamilyBudget — Project Context for Claude Code

## Purpose

Learning project: deepen C# / ASP.NET Core / EF Core / PostgreSQL skills through building a real, deployed web app for tracking family expenses. The learning goal matters as much as the working product — the developer (owner of this repo) wants to understand and write the core business logic herself; Claude Code is used for scaffolding, mechanical/repetitive work, debugging assistance, and validation (running builds/tests), not for writing the conceptual/business-logic code end-to-end on her behalf unless explicitly asked.

Full specification, phased plan, and DB schema are in:
@TZ_semeynyi_budget_en.md
@PLAN_semeynyi_budget_en.md
@DB_SCHEMA_semeynyi_budget_en.md

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

## Current status (update this section as stages complete)

Completed: Stage 0 (tooling), Stage 1 (Hello World skeleton + CI/CD + Render deploy), Stage 2 (DB schema design), Stage 3 (EF Core models/DbContext/first migration), Stage 4 (Neon connected to Render).

In progress — Stage 5 (Authentication + family groups): `ApplicationUser`/`IdentityDbContext` migrated and applied (local + Neon). Identity scaffolded via `dotnet aspnet-codegenerator identity` (Login/Register/Logout only, as Razor Pages under `Areas/Identity/Pages/Account`). `Program.cs` updated (`AddIdentity`, `AddRazorPages`/`MapRazorPages`, `UseAuthentication`/`UseAuthorization`, `AddCascadingAuthenticationState`). Fixed a broken `Layout` path in `_ViewStart.cshtml` (generator assumed classic Razor Pages folder structure, not Blazor Web App's `Components/Layout`) and added a minimal `Areas/Identity/Pages/Shared/_Layout.cshtml`. Not yet verified end-to-end (register → login → logout flow untested as of this writing). Family group creation/joining screens not started. Google OAuth not started.

Not started: Stage 6 (purchase tracking UI), Stage 7 (reports), Stage 8 (end-to-end deployed run-through), Stage 9 (Cowork CSV import), Stage 10 (tests).

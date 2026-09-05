# Work Plan: Family Expense Tracker Web App

Plan logic: first, a working "skeleton" (Hello World, deployed, with CI), then the foundation (DB schema as a separate discussion), and only after that — functionality layer by layer.

## Stage 0. Tooling Setup (one-time) ✅ Done

| Tool | Why | Link |
|---|---|---|
| Git | version control | git-scm.com |
| .NET 10 SDK | current LTS version (November 2025, supported until November 2028) | dotnet.microsoft.com |
| Docker Desktop | local PostgreSQL in a container | docker.com |
| VS Code + C# Dev Kit extension **or** Visual Studio Community 2026 | editor/IDE | both free for individual use |
| dotnet-ef (CLI tool) | EF Core migrations | `dotnet tool install --global dotnet-ef` |
| GitHub account | repository + CI (GitHub Actions) | github.com |
| Render account | app hosting | already discussed |
| Neon account | PostgreSQL hosting | already discussed |

Post-install check: `dotnet --version` shows 10.x, `docker --version` works, `git --version` works.

### Per-tool detail

| Tool | How to check | What it should be | If not installed | If wrong version |
|---|---|---|---|---|
| Git | `git --version` | any reasonably recent version (2.40+) | download from git-scm.com and install | update via the git-scm.com installer — git version compatibility rarely breaks, critical issues are rare |
| .NET SDK | `dotnet --version` | 10.x.x | download the SDK (not the Runtime!) from dotnet.microsoft.com/download | no problem — different SDK versions install side by side without conflicting; just install the .NET 10 SDK in addition, old ones can stay |
| Docker Desktop | `docker --version`, then `docker ps` (to confirm Docker itself is actually running, not just installed) | any current version; more importantly, `docker ps` shouldn't error out | download from docker.com and install (on Windows requires enabling WSL2) | update via Docker Desktop itself (it prompts about updates) or reinstall from the site |
| VS Code + C# Dev Kit **or** Visual Studio Community 2026 | VS Code: `code --version`; VS: Help → About Microsoft Visual Studio | latest stable version; for VS Code — be sure to install the "C# Dev Kit" extension separately | download from code.visualstudio.com or visualstudio.com | update via the editor's built-in updater |
| dotnet-ef (CLI) | `dotnet ef --version` | a version compatible with the installed .NET SDK (for .NET 10 — EF Core tools 10.x) | `dotnet tool install --global dotnet-ef` | `dotnet tool update --global dotnet-ef` |
| GitHub account | log in at github.com | active account (no "version" as such) | sign up at github.com | — |
| Render account | log in at render.com | active account | sign up at render.com | — |
| Neon account | log in at neon.tech | active account | sign up at neon.tech | — |

## Stage 1. Hello World + git + CI/CD Skeleton ✅ Done (2026-09-03)

Your requirement was CI/CD and deployment from the very first commit, even before there's anything real inside. We do this before any actual functionality.

1. **Create an empty Blazor Server project** (`dotnet new blazor -o FamilyBudget --interactivity Server`) and confirm it runs locally (`dotnet run`) — you should see the default page in the browser.
2. **Initialize git**, add a `.gitignore` for .NET, push to a new GitHub repository.
3. **Minimal CI**: a GitHub Actions workflow that runs `dotnet restore` + `dotnet build` on every push. No tests yet — those come in Stage 10.
4. **Deploy to Render**: Render has no native .NET runtime (native runtimes are Node.js, Python, Ruby, Go, Rust, Elixir only), so we deploy via Docker — a `Dockerfile` is needed at the repo root. Connect the repository, confirm the default Blazor page opens at the public URL.
5. **Set up a project in Neon** and save the connection string (as a GitHub secret / a Render environment variable) — just prepare it, not wired into the code yet since the DB hasn't been designed.

**Stage checkpoint:** there's a repository, CI runs a build on every commit, the app (even empty) is actually reachable over the internet at a URL. From here on, functionality can be added with confidence that the path to production already works.

## Stage 2. Database Schema Design (a separate discussion, no code) ✅ Done (2026-09-04)

Here we sit down and draw up the schema — entities, relationships, fields, indexes — before any code. Based on the spec:
- User, FamilyGroup, user membership in a group
- Category (self-referencing, arbitrary depth)
- Product, Store, Purchase
- Along the way, we close out the open questions from the spec: multi-group membership, invitation format.

The stage's output is an agreed-upon schema (as text or a diagram), which we return to in Stage 3.

## Stage 3. EF Core: Code From the Schema ✅ Done (2026-09-04)

1. NuGet packages: `Npgsql.EntityFrameworkCore.PostgreSQL`, `Microsoft.EntityFrameworkCore.Design`.
2. Entity classes and `DbContext` — a direct implementation of the Stage 2 schema.
3. Spin up local PostgreSQL via Docker (`docker run` or `docker compose` — the modern syntax with a space, no hyphen; the old standalone `docker-compose` is considered legacy and may not be present on the system).
4. First migration: `dotnet ef migrations add InitialCreate`, apply to the local DB (`dotnet ef database update`).
5. Check: the app starts, connects to the local DB, tables are created.

## Stage 4. Connecting Neon to Render (right after Stage 3, before features) ✅ Done (2026-09-04)

Moved up deliberately: without this step, any feature that touches the DB would break the Render deployment, violating the Stage 1 principle that the deployment always stays working.

1. Apply the already-ready migration to Neon (`dotnet ef database update`, pointing at the production connection string instead of the local one).
2. Set the Neon connection string as an environment variable in Render — `ConnectionStrings__DefaultConnection` (double underscore — how ASP.NET Core recognizes nested settings from environment variables).
3. The secret is never committed to git — only as an environment variable in Render itself.

**Why this doesn't interfere with local development:** `appsettings.Development.json` is only read when `ASPNETCORE_ENVIRONMENT=Development` (the default when running `dotnet run` locally). On Render, `ASPNETCORE_ENVIRONMENT=Production`, so the development file isn't read there at all — only the environment variable is used. Two independent, non-overlapping paths — local development (Docker Postgres) and production (Neon) don't interfere with each other.

## Stage 5. Authentication + Family Groups

**Architectural note (found while preparing for this stage):** the hand-rolled `User` model from Stage 3 conflicts with ASP.NET Core Identity — reworking it into `ApplicationUser : IdentityUser<int>` (removing the hand-rolled `Email`/`PasswordHash`/`GoogleId`; Google sign-in will go through the built-in `UserLogins` table). The `DbContext` changes from a plain one to `IdentityDbContext<ApplicationUser, IdentityRole<int>, int>`. This requires recreating the migration from scratch (locally via `dotnet ef migrations remove`, on Neon via `DROP SCHEMA public CASCADE; CREATE SCHEMA public;`, since there's no real data yet).

**Second architectural detail (also found in advance):** Login/Register/Logout can't be regular interactive Blazor components (`--interactivity Server`) — Identity sets the auth cookie via the HTTP response, and by the time an interactive component "comes alive," response headers have already been sent. The solution — a documented Microsoft pattern: these 2-3 pages are built as static SSR Razor pages (as in the `dotnet new blazor -au Individual` template), while the rest of the app stays Interactive Server as originally planned.

1. Recreate the migration for the new model (`ApplicationUser`, `IdentityDbContext`).
2. ASP.NET Core Identity with email/password — Login/Register/Logout as static SSR pages (see above), with no external dependencies, so as not to be blocked on setting up Google.
3. Screens for creating a family group and joining an existing one.
4. As a separate sub-step — add Google OAuth on top of the already-working Identity authentication (registering an OAuth client in Google Cloud Console; technically this is the built-in `UserLogins` table, not a hand-rolled field).

## Stage 6. Purchase Tracking

1. Forms for adding/editing a purchase (date, amount, category, note) on Blazor pages.
2. Category management: creation, arbitrary-depth nesting, seeding a default category set when a group is created.

## Stage 7. Reports (MVP)

1. Spending breakdown by category/subcategory over an arbitrary period — a query via LINQ (`GroupBy`), possibly with one "raw" SQL query for more complex aggregation.
2. A report page in Blazor with a date-range picker.

## Stage 8. End-to-End Run on the "Live" Deployment

1. A full end-to-end run on the deployed version: registration → creating a group → adding a purchase → viewing a report.

## Stage 9. Import via Claude Cowork

1. Only now, knowing the final DB schema, do we fix the exact CSV format (columns, category-hierarchy encoding).
2. Import screen: file upload (`CsvHelper`), preview of parsed rows, a check for possible duplicates, confirmation, and bulk insert.
3. We formulate the instruction for Cowork for this specific format.

## Stage 10. Tests

1. xUnit + Testcontainers (spins up a real Postgres in a container for the duration of the tests).
2. Coverage priority: business logic — report calculations, duplicate-detection logic on import.
3. Add a "run tests" step to the existing CI workflow from Stage 1.

## Stage 11. Backlog (post-MVP, optional)

- Visualization (charts).
- Period comparison.
- Spending breakdown by family member.
- Roles/permissions within a group.
- Real multi-currency support.

## Open Question About the Plan

Resolved: the plan (together with the spec and the DB schema) goes into the repository — this is needed for `CLAUDE.md` and for working with Claude Code (see the corresponding section in `CLAUDE.md`).

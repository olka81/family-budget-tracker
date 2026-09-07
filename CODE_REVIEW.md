# Code Review — Overall Health Check vs. Documentation

Date: 2026-09-07
Branch: `feature/identity-pages`
Scope: full repo, verified against `CLAUDE.md`, `TZ_semeynyi_budget.md`, `PLAN_semeynyi_budget.md`, `DB_SCHEMA_semeynyi_budget.md`. Read-only — no files were modified except this one.

## 1. Summary

The codebase is in genuinely good shape and matches `CLAUDE.md`/the spec docs closely for everything that's supposed to be done through Этап 5. Models, the `DbContext`, the design-time factory, `Program.cs`, and the applied migration are all internally consistent with each other and with the documented architecture decisions — I did not find drift between the EF model and the migration snapshot, which is the most common way these docs quietly go stale. `dotnet build` is clean (0 errors, only the pre-existing low-severity `NU1901` NuGet advisories, as documented). The one currently-uncommitted change (`Register.cshtml.cs`) matches exactly what `CLAUDE.md` describes as already done. Two things are worth the developer's attention even though they don't block anything: (1) `Login.cshtml` has two unconditional links (`ForgotPassword`, `ResendEmailConfirmation`) to pages that were never scaffolded — clicking them 404s, and this isn't the same, already-documented `LoginWith2fa`/`Lockout` latent issue in `NOTES.md`; and (2) the DB schema doc states `Product.Name` should be unique within a `FamilyGroup`, but no unique constraint exists anywhere in the model or migration — worth deciding now while there's no real data, since retrofitting a unique index later means resolving existing duplicates first.

## 2. Findings by area

### 1. Models vs. DB schema doc — confirmed OK
`FamilyGroup`, `ApplicationUser`, `Category`, `Product`, `Store`, `Purchase`, `PaymentMethod` all match the schema doc's fields, types, and nullability exactly (`decimal` for money, `DateOnly` for `Purchase.Date`, `Currency` as an explicit string column defaulting `"EUR"`, nullable `CategoryId`/`StoreId`/`ParentCategoryId`, self-referencing `Category`). One gap relative to the schema doc, see below.

**Finding — `Product.Name` uniqueness not enforced.** `DB_SCHEMA_semeynyi_budget.md` states for `Product.Name`: "уникальность в рамках FamilyGroupId". Neither `Product.cs`, `FamilyBudgetDbContext.OnModelCreating`, nor the `InitialCreate` migration define a unique index (composite or otherwise) on `(FamilyGroupId, Name)`. Today this is inert since there's no product-creation UI yet (Этап 6 not started), but it means the schema's stated invariant currently has zero enforcement — worth adding a `HasIndex(p => new { p.FamilyGroupId, p.Name }).IsUnique()` before or alongside the purchase-tracking UI work, since adding it after real data exists requires a dedup pass first.

### 2. `FamilyBudgetDbContext.cs` — confirmed OK
Inherits `IdentityDbContext<ApplicationUser, IdentityRole<int>, int>` correctly and calls `base.OnModelCreating(modelBuilder)` first. Explicit delete-behavior configuration is present exactly where the schema doc calls out non-obvious app-level rules: `Category` self-reference uses `Restrict` (consistent with "cycle prevention must be app-level, not DB" — a `Restrict` here doesn't prevent cycles by itself, but no DB constraint can; that logic still needs to live in whatever code eventually mutates `ParentCategoryId`, which doesn't exist yet since category management isn't started). `Product.Category`, `Purchase.Store` use `SetNull` (both nullable FKs). `Purchase.Product`, `Purchase.User` use `Restrict`. All `FamilyGroupId` FKs (on `ApplicationUser`, `Category`, `Product`, `Store`, `Purchase`) are left on EF Core's convention default, which the migration confirms resolved to `Cascade` — reasonable and not contradicted by the schema doc, which doesn't specify a behavior for those.

### 3. `FamilyBudgetDbContextFactory.cs` — confirmed OK
Matches Architecture Decision #8 exactly: `IDesignTimeDbContextFactory<FamilyBudgetDbContext>`, reads `appsettings.Development.json` via `AddJsonFile` then layers `AddEnvironmentVariables()` on top (env vars override the file, since `ConfigurationBuilder` sources added later win), pulls `ConnectionStrings:DefaultConnection`, uses `UseNpgsql`.

### 4. Migrations — confirmed OK, no drift
Compared `InitialCreate.cs` and `FamilyBudgetDbContextModelSnapshot.cs` directly against the current model classes and `OnModelCreating`. Every table, column, FK, delete behavior, and index in the migration matches what the current models/DbContext would generate — including the `IdentityByDefaultColumn` value-generation strategy, the `Restrict`/`SetNull`/`Cascade` choices from item 2, and all the FK indexes. No orphaned columns, no missing indexes, nothing the models declare that the migration lacks or vice versa.

### 5. `Program.cs` — confirmed OK
- **#5**: `AddIdentity<ApplicationUser, IdentityRole<int>>(options => options.SignIn.RequireConfirmedAccount = false).AddEntityFrameworkStores<FamilyBudgetDbContext>().AddDefaultTokenProviders()` — exactly as specified, not `AddDefaultIdentity`.
- **#6**: `AddRazorPages()`/`MapRazorPages()` present alongside `AddRazorComponents()`/`MapRazorComponents<App>().AddInteractiveServerRenderMode()` — both pipelines coexist as intended.
- **#7**: `AddDbContextFactory<FamilyBudgetDbContext>(...)` — not `AddDbContext`.
- **#11**: `builder.Services.AddSingleton<Microsoft.AspNetCore.Identity.UI.Services.IEmailSender, Microsoft.AspNetCore.Identity.UI.Services.NoOpEmailSender>();` — fully qualified, matches exactly.
- Middleware order: `UseHttpsRedirection()` → `UseAuthentication()` → `UseAuthorization()` → `UseAntiforgery()` → `MapStaticAssets()`/`MapRazorPages()`/`MapRazorComponents()` — correct order for both Identity cookie auth and antiforgery to work.
- `AddCascadingAuthenticationState()` is registered but currently has no consumer — no Blazor component in `Components/` reads `AuthenticationStateProvider` yet (see item 10 below). Harmless now, just unused scaffolding until Этап 6+.

### 6. Identity area
- **Layout structure — confirmed OK.** `Areas/Identity/Pages/_ViewStart.cshtml` sets `Layout = "_Layout";`, and `Areas/Identity/Pages/Shared/_Layout.cshtml` exists with `_LoginPartial` wired in via `Engine.FindView` + `RenderPartialAsync`, matching what `CLAUDE.md` describes as done. No orphaned root `Pages/` folder — confirmed via glob, zero matches.
- **`Register.cshtml.cs` transaction/FamilyGroup logic — confirmed OK, matches `CLAUDE.md` exactly.** Diffed the current uncommitted working-tree change against the last commit: it adds the `FamilyBudgetDbContext` constructor dependency, wraps `FamilyGroup` creation + `SaveChangesAsync()` + `_userManager.CreateAsync(...)` in `BeginTransactionAsync()`/`CommitAsync()` (commit only on `result.Succeeded`), sets `user.FamilyGroupId` from the newly-created group's `Id` before calling `CreateAsync`, and removes the dead scaffolded email-confirmation block (`GenerateEmailConfirmationTokenAsync`, `ConfirmEmail` callback URL, `_emailSender.SendEmailAsync`) along with the now-unreachable `RequireConfirmedAccount` branch. `SetUserNameAsync`/`SetEmailAsync` still run before `CreateAsync`, as required. This is exactly what `CLAUDE.md`'s "Current status" section claims — confirmed against the real file, not just the doc's word.
- **`LoginWith2fa`/`Lockout` — confirmed still just a documented latent issue, unchanged.** `Login.cshtml.cs` still redirects to `./LoginWith2fa` and `./Lockout` on the corresponding `SignInResult` branches; neither page exists (only Login/Register/Logout were scaffolded). This exactly matches the risk already written up in `NOTES.md`'s last bullet (2FA never enabled, but `PasswordSignInAsync` checks pre-existing lockout regardless of `lockoutOnFailure: false`, so a locked-out account would hit a 404 instead of a lockout message). Nothing new here — flagging only to confirm it wasn't silently changed.
- **New finding, not previously documented: `Login.cshtml` has two more dead links, unconditionally rendered.** Lines 37 (`asp-page="./ForgotPassword"`) and 43 (`asp-page="./ResendEmailConfirmation"`) render unconditionally (unlike the external-login section, which is gated behind `ExternalLogins.Count == 0` and therefore never renders since no external provider is configured). Neither `ForgotPassword.cshtml` nor `ResendEmailConfirmation.cshtml` exists anywhere in `Areas/Identity/Pages/Account/` — confirmed via glob (only `Login`, `Logout`, `Register` exist). Any real visitor to `/Identity/Account/Login` today sees two links that 404 on click. This is distinct from the `LoginWith2fa`/`Lockout` issue (those only trigger on specific sign-in outcomes; these two render on every page load) and isn't mentioned in `NOTES.md` or `CLAUDE.md`. Low severity (cosmetic/UX, not a security or data issue) but worth a one-line fix (delete the two `<p>` blocks) whenever the developer is next in this file, or scaffolding the missing pages if forgot-password support is wanted later.
- **Static assets — confirmed the "known cosmetic issue" is accurate, and slightly more broken than documented.** `CLAUDE.md` says the layout references `~/Identity/lib/bootstrap/...` paths that don't exist, causing unstyled forms. Confirmed: `wwwroot/` has no `Identity/` subfolder at all (glob returned nothing), so every `~/Identity/...` reference in `_Layout.cshtml` (CSS, JS, jQuery) 404s. Additionally — not previously called out — `_ValidationScriptsPartial.cshtml` references `~/lib/jquery-validation/dist/...` (correct path convention, no `Identity/` prefix) but `wwwroot/lib/` only contains `bootstrap/`; there is no `jquery-validation` or plain `jquery` folder anywhere in `wwwroot`. So beyond the documented "unstyled forms," client-side (unobtrusive) validation JS is entirely absent too — validation currently only happens server-side on postback. Same underlying deferred fix, just worth knowing the actual blast radius is "no styling AND no client-side validation," not just styling.

### 7. `appsettings.json` / `appsettings.Development.json` — confirmed OK
`appsettings.json` (production) has no connection string at all — correct, since production reads `ConnectionStrings__DefaultConnection` purely from the Render environment variable per the documented design. `appsettings.Development.json` contains only the local Docker Postgres connection string (`Host=localhost;...;Password=devpassword`), which matches `NOTES.md`'s documented local dev setup — this is a throwaway local-only password for a disposable Docker container, not a real secret, so committing it is consistent with the project's own stated design (not an oversight). `.gitignore` is the standard `dotnet new gitignore` and does not special-case `appsettings.Development.json` — meaning it's committed by design, matching the docs. No real secrets or Neon/production connection strings found in any committed file.

### 8. `Dockerfile` — confirmed OK
Multi-stage (`sdk:10.0` build → `aspnet:10.0` final), `EXPOSE 10000` + `ENV ASPNETCORE_HTTP_PORTS=10000`, `ENTRYPOINT ["dotnet", "FamilyBudget.dll"]` — matches `PLAN`/`NOTES.md`.

### 9. `.github/workflows/ci.yml` — confirmed OK
Triggers on `push`/`pull_request` to `main` only, steps are `actions/checkout` → `actions/setup-dotnet` (`10.0.x`) → `dotnet restore` → `dotnet build --no-restore`. No test step, consistent with Этап 10 not started.

### 10. General / other observations
- **Main Blazor app has zero integration with Identity so far.** `Components/Layout/MainLayout.razor` and `NavMenu.razor` are still the unmodified `dotnet new blazor` scaffold (Home/Counter/Weather links, no auth-aware UI, no link to `/Identity/Account/Login`, no `[Authorize]` anywhere, no consumer of `AuthenticationStateProvider`). A user can browse the entire main app anonymously and has no in-app way to reach the login page except by typing the URL. This is expected given Этап 6+ hasn't started, not a bug — flagging it as a known gap rather than a defect. See open question below.
- Two prior code-review report files (`CODE_REVIEW_STAGE5.md`, `CODE_REVIEW_STAGE5_ROUND2.md`) are deleted in the working tree per `git status`, alongside edits to `CLAUDE.md`/`NOTES.md`; this review doesn't touch that decision, just noting it's part of the current uncommitted diff.
- Package versions in `FamilyBudget.csproj` are internally consistent (`Microsoft.AspNetCore.Identity.*` 10.0.11, `Microsoft.EntityFrameworkCore.*` 10.0.11, `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.3, `Nullable`/`ImplicitUsings` enabled) — nothing stale or mismatched found.

## 3. Build result

```
Determining projects to restore...
D:\Cowork\PetProject\FamilyBudget\FamilyBudget.csproj : warning NU1901: Package 'NuGet.Packaging' 6.12.1 has a known low severity vulnerability, https://github.com/advisories/GHSA-g4vj-cjjj-v7hg
D:\Cowork\PetProject\FamilyBudget\FamilyBudget.csproj : warning NU1901: Package 'NuGet.Protocol' 6.12.1 has a known low severity vulnerability, https://github.com/advisories/GHSA-g4vj-cjjj-v7hg
  All projects are up-to-date for restore.
D:\Cowork\PetProject\FamilyBudget\FamilyBudget.csproj : warning NU1901: Package 'NuGet.Packaging' 6.12.1 has a known low severity vulnerability, https://github.com/advisories/GHSA-g4vj-cjjj-v7hg
D:\Cowork\PetProject\FamilyBudget\FamilyBudget.csproj : warning NU1901: Package 'NuGet.Protocol' 6.12.1 has a known low severity vulnerability, https://github.com/advisories/GHSA-g4vj-cjjj-v7hg
  FamilyBudget -> D:\Cowork\PetProject\FamilyBudget\bin\Debug\net10.0\FamilyBudget.dll

Build succeeded.

D:\Cowork\PetProject\FamilyBudget\FamilyBudget.csproj : warning NU1901: Package 'NuGet.Packaging' 6.12.1 has a known low severity vulnerability, https://github.com/advisories/GHSA-g4vj-cjjj-v7hg
D:\Cowork\PetProject\FamilyBudget\FamilyBudget.csproj : warning NU1901: Package 'NuGet.Protocol' 6.12.1 has a known low severity vulnerability, https://github.com/advisories/GHSA-g4vj-cjjj-v7hg
D:\Cowork\PetProject\FamilyBudget\FamilyBudget.csproj : warning NU1901: Package 'NuGet.Packaging' 6.12.1 has a known low severity vulnerability, https://github.com/advisories/GHSA-g4vj-cjjj-v7hg
D:\Cowork\PetProject\FamilyBudget\FamilyBudget.csproj : warning NU1901: Package 'NuGet.Protocol' 6.12.1 has a known low severity vulnerability, https://github.com/advisories/GHSA-g4vj-cjjj-v7hg
    4 Warning(s)
    0 Error(s)

Time Elapsed 00:00:01.29
```

No warnings beyond the four (two distinct advisories, each listed twice for restore+build) `NU1901` NuGet advisories already known and documented.

## 4. Open questions for the human

1. **`Product.Name` uniqueness (item 1 above):** do you want the unique index added now (cheap, no data exists yet) or deliberately deferred until the purchase-tracking UI (Этап 6) is being built, since that's when `Product` creation logic actually gets written? Either is reasonable, but it's a judgment call on sequencing, not something I should just add given this was a read-only review.
2. **Dead `Login.cshtml` links (`ForgotPassword`, `ResendEmailConfirmation`):** delete the links, or scaffold the missing pages? Depends on whether forgot-password is wanted before Google OAuth (per plan, OAuth is the next Identity subtask, not password reset) — your call.
3. **Main app / Identity integration gap (item 10):** is showing a login/logout link in the main Blazor app's nav intentionally deferred to a specific later step, or was it assumed to already be wired up? Nothing in `PLAN`/`CLAUDE.md` explicitly schedules this, so flagging rather than assuming.

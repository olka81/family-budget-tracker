# Code Review — Stage 5 (Authentication + Family Groups)

Reviewed against `CLAUDE.md`, `TZ_semeynyi_budget_en.md`, `PLAN_semeynyi_budget_en.md`, `DB_SCHEMA_semeynyi_budget_en.md`, and `NOTES.md`. Scope: everything touched by the Identity scaffolding work (`Program.cs`, `Models/`, `Data/`, `Migrations/`, `Areas/Identity/Pages/Account/*`, `appsettings*.json`, plus the static-asset/layout files the scaffolder generated).

## 1. Summary

The project does not currently build, so the register → login → logout flow has not just "not been verified end-to-end" as `CLAUDE.md` says — it cannot run at all in its present state. More importantly, comparing `git diff HEAD -- Program.cs` against what `CLAUDE.md` and `NOTES.md` describe as already done shows that **none of the described manual fixes to `Program.cs` are actually present on disk**: the file contains only the two lines `dotnet aspnet-codegenerator identity` inserts automatically (the `using Microsoft.AspNetCore.Identity;` and the `AddDefaultIdentity<ApplicationUser>(...)` call), with a missing `using` that breaks the build outright. The `_ViewStart.cshtml` layout fix described in `CLAUDE.md`/`NOTES.md` also doesn't match what's on disk — a different, coincidentally-working fix exists instead, in a different location than documented. Beyond the wiring, there is no code anywhere that creates the "personal group of one" `FamilyGroup` the spec requires at registration, so even a fully-wired Identity setup would fail the moment a user tries to register, with a foreign-key violation. None of this is surprising for an in-progress stage, but it means the actual code is meaningfully behind what the status notes claim — worth reconciling before continuing.

## 2. Blocking issues

Ordered roughly in the sequence you'd hit them if you tried to run register → login → logout right now.

### 2.1 Project does not compile
**Where:** `Program.cs:15`
**What:** `dotnet build` fails with `CS0246: The type or namespace name 'ApplicationUser' could not be found`. `Program.cs` uses `ApplicationUser` (in `AddDefaultIdentity<ApplicationUser>(...)`) but only has `using FamilyBudget.Components;`, `using FamilyBudget.Data;`, `using Microsoft.EntityFrameworkCore;`, `using Microsoft.AspNetCore.Identity;` at the top — there's no `using FamilyBudget.Models;`, which is where `ApplicationUser` lives.
**Why it matters:** Nothing else in this review could be verified by actually running the app, because the build fails before that's possible. CI (`dotnet build` on push/PR) would also currently fail on this branch.
**What it should be:** Add `using FamilyBudget.Models;` to `Program.cs`. (Not applying this myself, per the read-only scope of this review — but this is the one blocker every other item downstream assumes is fixed.)

### 2.2 No `Areas/Identity/Pages/Account` Razor Pages endpoint is ever mapped
**Where:** `Program.cs` — entire middleware/endpoint section, lines 17-35.
**What:** There is no `app.MapRazorPages()` call anywhere in the pipeline. `app.MapRazorComponents<App>().AddInteractiveServerRenderMode()` (line 32) only maps Blazor component routes — it has no effect on classic Razor Pages routing. Whether or not `AddRazorPages()` is registered as a *service* (it may be, transitively, via `AddDefaultIdentity(...).AddDefaultUI()`, which is known to call it — worth confirming, since it isn't called explicitly here either), registering the service is not sufficient: `MapRazorPages()` is what adds the actual endpoints to the routing table.
**Why it matters:** Without it, `/Identity/Account/Login`, `/Identity/Account/Register`, and `/Identity/Account/Logout` will all 404, independent of every other issue in this list.
**What it should be:** `CLAUDE.md`'s "in progress" notes and the "known issues" list in the task both describe `AddRazorPages()` / `MapRazorPages()` as already added — they aren't present in the file.

### 2.3 No authentication/authorization middleware in the pipeline
**Where:** `Program.cs`, lines 17-35.
**What:** Neither `app.UseAuthentication()` nor `app.UseAuthorization()` is called. The described order (`UseAuthentication()` / `UseAuthorization()`, before `UseAntiforgery()` and endpoint mapping) is documented as done in `CLAUDE.md` and the task's "known issues" list, but isn't in the file.
**Why it matters:** `AddDefaultIdentity(...)` registers the cookie authentication *scheme* as a service, but without `UseAuthentication()` in the pipeline, no middleware actually reads the auth cookie on incoming requests and populates `HttpContext.User`. Practical effect: even after a successful `SignInManager.PasswordSignInAsync(...)` sets the cookie, the very next request will still see an anonymous user — `SignInManager.IsSignedIn(User)` in `Pages/Shared/_LoginPartial.cshtml` would always evaluate false. Separately, `UseAuthorization()`'s absence means any future `[Authorize]`-protected page (family group screens, purchase pages — the next things on the Stage 5/6 roadmap) will throw at request time ("...contains authorization metadata... but a middleware was not found that supports authorization enforcement checks...").

### 2.4 No `FamilyGroup` is created when a user registers
**Where:** `Areas/Identity/Pages/Account/Register.cshtml.cs`, `CreateUser()` (lines 158-170) and `OnPostAsync()` (lines 110-156); `Models/ApplicationUser.cs` (`FamilyGroupId` is a non-nullable `int` with no default).
**What:** `CreateUser()` does `Activator.CreateInstance<ApplicationUser>()` with no `FamilyGroupId` assignment. Nothing else in `Register.cshtml.cs`, `Models/`, or anywhere else in the codebase (confirmed via a repo-wide search for `new FamilyGroup` / `FamilyGroupId =` / `FamilyGroups.Add`) creates a `FamilyGroup` row. A freshly constructed `ApplicationUser.FamilyGroupId` defaults to `0`.
**Why it matters:** This is Architecture Decision #1 in `CLAUDE.md` and spec section 2.2: "upon registration, a 'personal' group of one is automatically created... there is no separate 'no group' mode." As written, `_userManager.CreateAsync(user, Input.Password)` will attempt to insert an `AspNetUsers` row with `FamilyGroupId = 0`, which violates the `FK_AspNetUsers_FamilyGroups_FamilyGroupId` foreign key (no `FamilyGroup` with `Id = 0` exists) — the insert fails. `RegisterModel.OnPostAsync` has no try/catch around `CreateAsync`, so this surfaces as an unhandled `DbUpdateException`, not a graceful validation error.
**What it should be:** Per the plan, this is core to Stage 5's "screens for creating a family group and joining an existing one" work, but the *auto-create-on-registration* half specifically needs to happen inside (or immediately around) `RegisterModel.OnPostAsync`, before `_userManager.CreateAsync` is called — not deferred to a separate screen, since a user without a group should never exist even transiently. Flagging as blocking rather than "just not started yet" because it will hard-fail every registration attempt, not merely leave a feature incomplete.

### 2.5 Identity is registered as raw, unedited scaffolder output — contradicts the documented decision
**Where:** `Program.cs:15`: `builder.Services.AddDefaultIdentity<ApplicationUser>(options => options.SignIn.RequireConfirmedAccount = true).AddEntityFrameworkStores<FamilyBudgetDbContext>();`
**What:** `git diff HEAD -- Program.cs` shows this line (and the one `using` line above it) is *exactly* what `dotnet aspnet-codegenerator identity` inserts automatically — it has not been hand-edited since scaffolding ran. This directly contradicts:
- `CLAUDE.md` Architecture Decision #5: "use `AddIdentity<ApplicationUser, IdentityRole<int>>()...AddDefaultTokenProviders()`, NOT `AddDefaultIdentity`... `RequireConfirmedAccount` must stay `false`."
- `NOTES.md` line 42-43 (same two points, in Russian, written independently).
**Why it matters, concretely:**
- `RequireConfirmedAccount = true` with no `IEmailSender` actually configured (the scaffolder wires `Microsoft.AspNetCore.Identity.UI`'s default no-op `EmailSender`, which never delivers mail) means: after a (hypothetically successful) registration, `RegisterModel.OnPostAsync` redirects to `RedirectToPage("RegisterConfirmation", ...)` (line 140) — but no `RegisterConfirmation.cshtml` page exists; only `Login`, `Register`, and `Logout` were scaffolded. That's a 404 on every successful registration.
- Even setting that aside, since no confirmation email is ever actually delivered, the account can never be confirmed, so `PasswordSignInAsync` will permanently refuse to sign the user in ("not allowed" result, distinct from "failed") — the flow is a dead end from the first registration onward.
- `AddDefaultIdentity<ApplicationUser>()` (single type parameter) does not register a role type at all — no `.AddRoles<IdentityRole<int>>()` is chained. But `FamilyBudgetDbContext : IdentityDbContext<ApplicationUser, IdentityRole<int>, int>` already has the `AspNetRoles`/`AspNetUserRoles`/`AspNetRoleClaims` tables keyed on `int` (see migration `20260904184410_InitialCreate.cs`). This reproduces, in practice, the exact problem Decision #5 says motivated switching away from `AddDefaultIdentity` in the first place ("doesn't let us specify `IdentityRole<int>` explicitly") — `RoleManager<IdentityRole<int>>` is not currently resolvable via DI. Not exercised by any code yet, so not a live crash today, but it will fail the first time anything requests a `RoleManager<IdentityRole<int>>`.

### 2.6 `AddCascadingAuthenticationState()` is missing
**Where:** `Program.cs`, service registration section (lines 8-15).
**What:** Not called anywhere, though `CLAUDE.md`'s status notes list it as already added.
**Why it matters:** Nothing in the current Blazor component tree (`Components/`) reads auth state yet, so this isn't breaking anything *today*. But it's a prerequisite for the very next piece of Stage 5 — any Blazor component that needs to show "logged in as X" or gate content by family-group membership needs `<CascadingAuthenticationState>` wired up via this call. Listing it here rather than under "inconsistencies" because it blocks the next unit of work, not because it currently breaks something.

## 3. Inconsistencies with the spec / documented architecture

### 3.1 The documented `_ViewStart.cshtml` fix doesn't match what's on disk
`CLAUDE.md` and `NOTES.md` (line 45-46) both describe the fix the same way: change `Areas/Identity/Pages/_ViewStart.cshtml`'s `Layout` from `"/Pages/Shared/_Layout.cshtml"` to `"_Layout"`, and add a new `Areas/Identity/Pages/Shared/_Layout.cshtml`.

What's actually on disk:
- `Areas/Identity/Pages/_ViewStart.cshtml` still contains the **original, unedited** scaffolder line: `Layout = "/Pages/Shared/_Layout.cshtml";`.
- There is no `Areas/Identity/Pages/Shared/` folder at all.
- Instead, a parallel tree exists at the repo root: `Pages/_ViewStart.cshtml` (`Layout = "_Layout";`), `Pages/_ViewImports.cshtml`, `Pages/Shared/_Layout.cshtml`, `Pages/Shared/_LoginPartial.cshtml`, `Pages/Shared/_ValidationScriptsPartial.cshtml`.

Razor Pages under an Area resolve `_ViewStart.cshtml` by walking up *within that Area's own `Pages/` tree* (`Account/` → `Identity/Pages/`) — it does not continue climbing out to the app-root `Pages/` folder. So the file that actually governs `Login`/`Register`/`Logout`'s layout is `Areas/Identity/Pages/_ViewStart.cshtml`, and its absolute path `/Pages/Shared/_Layout.cshtml` does happen to resolve, since that file exists at the repo root. So — once 2.1/2.2 are fixed — this most likely still works, just not via the mechanism the docs describe. `Pages/_ViewStart.cshtml` and `Pages/_ViewImports.cshtml` at the repo root appear to be orphaned: there are no other physical Razor Pages anywhere under `Pages/` for them to apply to (everything else in this Blazor Web App lives under `Components/`).

Flagging as an inconsistency rather than a bug, since it likely works by coincidence — but the documentation and the code disagree about *why* it works, which will confuse future edits. See open question 5.2.

### 3.2 Program.cs Identity setup vs. Architecture Decision #5
Already covered in detail under 2.5 — listed there as blocking because of the concrete `RequireConfirmedAccount=true` + missing-page consequence, but it's equally an inconsistency between documented decision and actual code, in case it's triaged separately.

## 4. Non-blocking / cosmetic

### 4.1 Layout references static assets that don't exist at those paths
`Pages/Shared/_Layout.cshtml` references `~/Identity/lib/bootstrap/dist/css/bootstrap.css`, `~/Identity/css/site.css`, `~/Identity/js/site.js`, and `~/Identity/lib/jquery/dist/jquery.js` (dev and non-dev `<environment>` blocks). None of these exist under that path:
- Bootstrap is actually at `wwwroot/lib/bootstrap/...` — no `Identity/` prefix.
- There is no `site.css`, `site.js`, or `jquery` anywhere under `wwwroot/` at all.
- Separately, `Pages/Shared/_ValidationScriptsPartial.cshtml` references `~/lib/jquery-validation/...` and `~/lib/jquery-validation-unobtrusive/...`, also absent from `wwwroot/lib`.

This is the same class of bug as the `_ViewStart.cshtml` path issue (scaffolder assumed a folder layout — in this case, one where Identity's static assets get copied under `wwwroot/Identity/`  — that doesn't match this project). Effect once the blocking issues above are fixed: Login/Register/Logout will render as completely unstyled raw HTML, and client-side jQuery validation will silently no-op (server-side `ModelState` validation on POST still works, so forms remain functional, just unstyled and without instant client-side feedback).

### 4.2 NuGet audit warnings
`dotnet build` surfaces `NU1901` (low severity) for `NuGet.Packaging` and `NuGet.Protocol` 6.12.1 — transitive dependencies, most likely pulled in by `Microsoft.VisualStudio.Web.CodeGeneration.Design` (a design-time-only scaffolding tool package, not part of the runtime app). Low priority, but noting since it shows up on every build.

### 4.3 `FamilyGroup.CreatedAt` default is app-side, not DB-side
`Models/FamilyGroup.cs`: `public DateTime CreatedAt { get; set; } = DateTime.UtcNow;` — evaluated at object-construction time in C#, not enforced as a database default. Not a spec violation (the schema doc doesn't call for a DB-level default), just means any future insert path that doesn't go through this class's normal constructor (raw SQL, admin tooling, etc.) won't get a timestamp automatically.

## 5. Open questions for the human

### 5.1 Where did the documented `Program.cs` edits go?
`CLAUDE.md` and `NOTES.md` both describe several specific `Program.cs` edits (Identity registration, middleware order, cascading auth state) as already done, but `git diff HEAD -- Program.cs` shows the file contains only the scaffolder's automatic output. Was this work done in an earlier session and lost — e.g. made, then overwritten by re-running `dotnet aspnet-codegenerator identity`, or made and never saved — or were the docs written prospectively (describing the intended edit) before the edit was actually applied? Worth checking chat history or `git stash`/reflog for anything recoverable before redoing this by hand, in case it turns out to just be a save/commit mishap rather than lost work.

### 5.2 Which `_Layout.cshtml` location is the "real" one going forward?
Given 3.1: do you want to keep `Pages/Shared/_Layout.cshtml` (repo root) as the actual, permanent Identity layout and update `CLAUDE.md`/`NOTES.md` to match, or move things to `Areas/Identity/Pages/Shared/_Layout.cshtml` as originally planned and fix `Areas/Identity/Pages/_ViewStart.cshtml` to point there? Both would work; this just decides which file the next round of edits targets, and which orphaned files (`Pages/_ViewStart.cshtml`, `Pages/_ViewImports.cshtml`) get cleaned up.

### 5.3 Cycle-prevention / same-family-group validation — confirmed absent, as expected
Per the schema doc's explicit callout, I checked specifically for application-level validation of (a) `Category.ParentCategoryId` cycle prevention and (b) `Purchase.UserId` belonging to the same `FamilyGroupId` as the purchase. Neither exists anywhere in the codebase (confirmed via search, not just absence-by-assumption). This matches "Stage 6 hasn't started" and isn't a bug — flagging per the task's instructions to confirm explicitly rather than silently skip it, since these are exactly the two rules the schema doc calls out as unenforceable by a plain DB constraint.

### 5.4 Is `IdentityRole<int>` actually needed for the MVP?
The spec (section 2.2) explicitly says "all group members are equal... no admin/regular-member split in the MVP." Given that, is `RoleManager`/`IdentityRole<int>` support worth fixing (via `AddIdentity<ApplicationUser, IdentityRole<int>>()...AddRoles<IdentityRole<int>>()` or equivalent) at all right now, or would it be simpler to drop `IdentityRole<int>` from `IdentityDbContext<ApplicationUser, IdentityRole<int>, int>` entirely until roles are actually needed post-MVP? This is a scope call, not something to guess at — noting it here since 2.5 surfaces the gap either way.

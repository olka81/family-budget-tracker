# Technical Specification: Family Expense Tracker Web App

## 1. Project Goal

The primary goal is educational: deepen C# skills (ASP.NET Core, EF Core) and relational database work (PostgreSQL) through a practical project. A secondary but real outcome is a working tool for tracking family expenses.

## 2. Functional Requirements

### 2.1. Authentication
- Sign in via email/password.
- Sign in via Google account (OAuth) — as the primary/preferred method.
- *Note:* both options are supported out of the box by ASP.NET Core Identity + an external provider, at no additional cost (a Google OAuth client is created for free in Google Cloud Console).

### 2.2. Family Groups
- A user can create a family group.
- A user can join an existing group (by invitation/code).
- **Member rights: all group members are equal** — any member can add, view, and edit all of the group's purchases. There is no admin/regular-member split in the MVP.
- **A user without a family:** upon registration, a "personal" group of one is automatically created for the user — there is no separate "no group" mode in the system. This simplifies the data model: purchases are always tied to a group rather than sometimes directly to a user, and moving from solo use to family use is simply inviting a second member into the already-existing group, with no data migration.
- **One user = exactly one group** — membership in multiple groups at once is not supported (decided at the schema design stage).

### 2.3. Purchase Tracking
- Adding a purchase: date, amount, merchant/description, category, subcategory, note.
- Categories form a hierarchy of **arbitrary depth** (category → subcategory → sub-subcategory, etc., with no hard two-level limit), predefined (a default set) with the ability to add custom ones.
- **Currency:** the MVP uses a single currency (EUR — Bulgaria has switched to the euro). The data structure is designed with future multi-currency support in mind (every purchase/account has an explicit currency field rather than a hardcoded value), but conversion and working with multiple currencies simultaneously are out of scope for the MVP.
- **Import via Claude Cowork:** receipt scans are processed in Cowork, producing a CSV file of purchases that is uploaded to the app as a whole through a dedicated import screen (with a preview before confirmation and a check for possible duplicates). A purchase, in general, must include: date, amount, merchant, category (accounting for arbitrary hierarchy depth), and a note. **The exact CSV format (column set, category-path encoding, delimiters) will be finalized after the DB schema is designed** — the import format must follow from the data schema, not the other way around.

### 2.4. Reports
MVP priority — one report, the rest goes to the backlog:
1. **(MVP) Spending breakdown by category for a selected period** — amount and share per category/subcategory over an arbitrary date range.
2. (Backlog, post-MVP) Period comparison — e.g., this month vs. last month.
3. (Backlog, post-MVP) Breakdown by family member — who spent how much.

### 2.5. Visualization
- Out of scope for the MVP. Considered as a follow-up stage once reports work in text/tabular form.

## 3. Non-Functional Requirements

- **Budget:** software licenses — free only, no exceptions. For hosting — free tiers take priority; if a free tier turns out to be insufficient (e.g., Render/Neon limits become a problem), choose the most budget-friendly paid option, not the first one that comes up.
- **Do not store bank account data** — only purchase details (amount, category, merchant, date).
- The application is a web app (not desktop), accessed via a browser.
- The project must be **deployable** (not just a local prototype) — this is part of its educational value.

## 4. Technology Stack (fixed earlier)

| Layer | Technology |
|---|---|
| Backend | ASP.NET Core |
| Frontend | Blazor Server (a deliberate choice — focus on the backend rather than learning a separate JS framework) |
| ORM | Entity Framework Core |
| Database | PostgreSQL |
| App hosting | Render (free tier) |
| DB hosting | Neon (persistent free tier) |
| Testing | xUnit + Testcontainers |
| Supporting AI tool | Claude Cowork — for parsing receipts into structured data |

## 5. Out of Scope for the MVP (future backlog)

- Multi-currency support as an actually working feature (only laid into the structure).
- Roles/permissions within a group (admin vs. member).
- Period comparison and per-member breakdown in reports.
- Visualization (charts).
- Mobile app / mobile-specific adaptation as a separate task.

## 6. Open Questions (to be resolved in later stages, not now)

- Exact DB table structure, field types, indexes — a separate step in the plan.
- Exact CSV format for import (columns, category-path encoding) — depends on the DB structure, to be decided right after it.
- Format of the group invitation (code, link, email invite).
- Duplicate-detection logic when importing from Cowork.

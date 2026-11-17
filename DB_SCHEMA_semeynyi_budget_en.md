# Database Schema: Family Expense Tracker Web App

Output of the Stage 2 discussion from the plan. The schema is conceptual (entities, fields, relationships), not tied to specific EF Core/SQL syntax — that's Stage 3.

## Key Decisions Made During the Discussion

- **Data isolation by family group**: Products, Categories, and Stores are not shared across the whole app — each FamilyGroup has its own.
- **Price is a property of the purchase, not the product**: Product is identified by name only (+ optionally a category); price is stored on Purchase, to avoid duplicating products whenever the price changes.
- **Purchase date is required**, defaulting to the current date at the time of entry.
- **No abstract buyers**: a purchase can only be attributed to a real user who is a member of the same group. The default is whoever enters the record.
- **One user = exactly one group** (this closes the open question from the spec about multi-group membership — membership in only one group at a time, not several simultaneously).
- **Group invitation is a single permanent link per group**, which can be regenerated. There is no separate “create a group” screen: a personal group is created on registration, and leaving a group creates a new personal “My Family”.

## Tables

### FamilyGroup
| Field | Type | Comment |
|---|---|---|
| Id | PK | |
| Name | text | |
| CreatedAt | timestamp | |
| InviteToken | uuid, **nullable**, unique index | invitation-link token (added in Stage 5, migration `AddFamilyGroupInviteToken`) |

**Invitation token:** `Guid?` in code. `NULL` means “no link yet”: the token is created with `Guid.NewGuid()` the first time the group page is opened. Regeneration writes a new `Guid.NewGuid()`. Multiple `NULL` values do not violate the unique index (PostgreSQL does not treat `NULL`s as equal).

### ApplicationUser
**Update from Stage 5:** replaces the previous hand-rolled `User`. Inherits from `IdentityUser<int>` (provides ready-made `Email`, `PasswordHash`, and an external-login mechanism via `UserLogins` — Google sign-in is no longer a hand-rolled `GoogleId` field, but a standard Identity scenario).

| Field | Type | Comment |
|---|---|---|
| Id | PK (int) | from IdentityUser<int> |
| Email | text | from IdentityUser |
| PasswordHash | text, nullable | from IdentityUser, managed by Identity |
| UserName | text | from IdentityUser (Identity keeps UserName and Email separate — they usually coincide in simple scenarios) |
| DisplayName | text | our custom field |
| FamilyGroupId | FK → FamilyGroup, **not null** | our custom field, exactly one group per user |

Plus the service tables that Identity adds automatically via `IdentityDbContext<ApplicationUser, IdentityRole<int>, int>`: `AspNetRoles`, `AspNetUserRoles`, `AspNetUserClaims`, `AspNetRoleClaims`, `AspNetUserLogins`, `AspNetUserTokens`.

### Category
| Field | Type | Comment |
|---|---|---|
| Id | PK | |
| Name | text | |
| ParentCategoryId | FK → Category, nullable | self-reference, arbitrary hierarchy depth |
| FamilyGroupId | FK → FamilyGroup, not null | |

**Default set (Stage 6):** every new group gets a tree of 26 categories (8 top-level); the only definition in code is `Services/DefaultCategories.cs`. Groups that existed before got the tree from the data migration `SeedDefaultCategories` (only groups with no categories; the migration holds a frozen copy of the list).

**Names** may repeat, including among sibling categories — there is no uniqueness constraint.

**Deleting a category** deletes its whole subtree (application logic, `CategoryService`). Products in the deleted categories get `CategoryId = NULL` (`ON DELETE SET NULL`); purchases are not affected.

### Product
| Field | Type | Comment |
|---|---|---|
| Id | PK | |
| Name | text | unique within a FamilyGroupId |
| CategoryId | FK → Category, nullable | may be unassigned |
| FamilyGroupId | FK → FamilyGroup, not null | |

### Store
| Field | Type | Comment |
|---|---|---|
| Id | PK | |
| Name | text | |
| FamilyGroupId | FK → FamilyGroup, not null | |

### Purchase
| Field | Type | Comment |
|---|---|---|
| Id | PK | |
| ProductId | FK → Product, not null | |
| Price | numeric(10,2), not null | the price of this specific purchase, not the product |
| Quantity | numeric, not null, default 1 | fractional values allowed |
| PaymentMethod | smallint, not null, default Card | enum: Card / Cash |
| Date | date, not null, default today | |
| StoreId | FK → Store, nullable | |
| UserId | FK → ApplicationUser, not null, default — whoever enters the record | choice limited to members of the same FamilyGroup |
| FamilyGroupId | FK → FamilyGroup, not null | intentionally duplicated — simplifies report queries |
| Currency | varchar(3), not null, default EUR | from the spec — groundwork for future multi-currency support, not actively used in the MVP |
| Notes | text, nullable | from the spec |

## Relationships — Summary

- FamilyGroup 1 → N ApplicationUser
- FamilyGroup 1 → N Category
- FamilyGroup 1 → N Product
- FamilyGroup 1 → N Store
- FamilyGroup 1 → N Purchase
- Category 1 → N Category (self, Parent/Child)
- Category 1 → N Product
- Product 1 → N Purchase
- Store 1 → N Purchase (nullable)
- ApplicationUser 1 → N Purchase

## Rules That Can't Be Expressed as a Plain DB Constraint (need application-level checks)

1. **Protection against cycles in the category hierarchy** (A → B → A). When changing ParentCategoryId, you need to check that the new parent's ancestors don't include the category itself — there's no simple SQL CHECK for arbitrary depth. Moving a category to another parent is not implemented yet.
2. **UserId on Purchase must belong to the same FamilyGroupId** as the purchase itself — a plain FK doesn't enforce this, it needs validation in code on save.
3. **Joining another group:** allowed only if the user is the sole member of their current group. Order: first move the user to the new group (change FamilyGroupId), then delete the old personal group together with its data. The reverse order is not possible: the user’s FK to the group is cascading, so deleting the group would delete the user. Both steps run in one transaction; at its start the row of the user’s current group is locked (`SELECT ... FOR UPDATE`), so that another person joining that group at the same moment cannot have their account deleted by the cascade.
4. **Leaving a group:** allowed only if the group has other members. In one transaction a new personal group “My Family” is created and the user is moved into it; the previous group and its data are left unchanged.

## Open Questions Left for Later (don't block Stage 3)

- Verified by a test (Stage 6): cascade-deleting a group with nested categories works despite the Restrict link to the parent category. Not verified: the Restrict relationships Purchase→Product and Purchase→User — joining a group relies on the cascade; to be checked once purchases exist.
- Not decided: what happens to the purchases of a member who has left a group (Purchase→User is Restrict).
- Exact CSV format for import via Cowork — now that the schema exists, this can be decided (Stage 9 per the plan).
- Duplicate-detection logic on import — also Stage 9.

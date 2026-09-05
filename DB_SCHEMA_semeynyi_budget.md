# Структура БД: веб-приложение учёта семейных расходов

Итог обсуждения Этапа 2 плана. Схема — концептуальная (сущности, поля, связи), без привязки к конкретному синтаксису EF Core/SQL — это будет Этап 3.

## Ключевые решения, зафиксированные в процессе обсуждения

- **Изоляция данных по семейным группам**: Продукты, Категории и Магазины — не общие на всё приложение, а свои у каждой FamilyGroup.
- **Цена — свойство покупки, а не продукта**: Product идентифицируется только названием (+ опционально категорией); цена хранится в Purchase, чтобы избежать дублирования продуктов при изменении цены.
- **Дата покупки обязательна**, по умолчанию — текущая дата на момент внесения.
- **Без абстрактных покупателей**: покупку можно приписать только реальному пользователю, состоящему в той же группе. По умолчанию — тот, кто вносит запись.
- **Один пользователь = ровно одна группа** (это закрывает открытый вопрос из ТЗ про множественное членство — членство только в одной группе, не в нескольких одновременно).

## Таблицы

### FamilyGroup
| Поле | Тип | Комментарий |
|---|---|---|
| Id | PK | |
| Name | текст | |
| CreatedAt | timestamp | |

### ApplicationUser
**Обновление с Этапа 5:** заменяет прежнюю самодельную `User`. Наследуется от `IdentityUser<int>` (даёт готовые `Email`, `PasswordHash`, механизм внешних логинов через `UserLogins` — Google-вход больше не самодельное поле `GoogleId`, а штатный сценарий Identity).

| Поле | Тип | Комментарий |
|---|---|---|
| Id | PK (int) | из IdentityUser<int> |
| Email | текст | из IdentityUser |
| PasswordHash | текст, nullable | из IdentityUser, управляется Identity |
| UserName | текст | из IdentityUser (Identity разделяет UserName и Email — в простых сценариях обычно совпадают) |
| DisplayName | текст | наше кастомное поле |
| FamilyGroupId | FK → FamilyGroup, **not null** | наше кастомное поле, ровно одна группа на пользователя |

Плюс служебные таблицы Identity, появляющиеся автоматически через `IdentityDbContext<ApplicationUser, IdentityRole<int>, int>`: `AspNetRoles`, `AspNetUserRoles`, `AspNetUserClaims`, `AspNetRoleClaims`, `AspNetUserLogins`, `AspNetUserTokens`.

### Category
| Поле | Тип | Комментарий |
|---|---|---|
| Id | PK | |
| Name | текст | |
| ParentCategoryId | FK → Category, nullable | самоссылка, произвольная глубина иерархии |
| FamilyGroupId | FK → FamilyGroup, not null | |

### Product
| Поле | Тип | Комментарий |
|---|---|---|
| Id | PK | |
| Name | текст | уникальность в рамках FamilyGroupId |
| CategoryId | FK → Category, nullable | может быть не назначена |
| FamilyGroupId | FK → FamilyGroup, not null | |

### Store
| Поле | Тип | Комментарий |
|---|---|---|
| Id | PK | |
| Name | текст | |
| FamilyGroupId | FK → FamilyGroup, not null | |

### Purchase
| Поле | Тип | Комментарий |
|---|---|---|
| Id | PK | |
| ProductId | FK → Product, not null | |
| Price | numeric(10,2), not null | цена именно этой покупки, не продукта |
| Quantity | numeric, not null, default 1 | дробное значение разрешено |
| PaymentMethod | smallint, not null, default Card | enum: Card / Cash |
| Date | date, not null, default сегодня | |
| StoreId | FK → Store, nullable | |
| UserId | FK → ApplicationUser, not null, default — тот, кто вносит запись | выбор ограничен участниками той же FamilyGroup |
| FamilyGroupId | FK → FamilyGroup, not null | продублировано намеренно — упрощает запросы для отчётов |
| Currency | varchar(3), not null, default EUR | из ТЗ — задел на будущую мультивалютность, в MVP не используется активно |
| Notes | текст, nullable | из ТЗ |

## Связи — сводка

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

## Правила, которые нельзя выразить обычным DB-constraint (нужна проверка на уровне приложения)

1. **Защита от зацикливания в иерархии категорий** (A → B → A). При изменении ParentCategoryId нужно проверять, что среди предков нового родителя нет самой категории — простого SQL CHECK для произвольной глубины не существует.
2. **UserId в Purchase должен принадлежать той же FamilyGroupId**, что и сама покупка — стандартный FK это не ограничивает, нужна валидация в коде при сохранении.

## Открытые вопросы, оставленные на будущее (не блокируют Этап 3)

- Формат приглашения в группу (код, ссылка, email) — из ТЗ, ещё не решено.
- Точный формат CSV для импорта через Cowork — теперь, когда схема есть, можно решать (Этап 9 по плану).
- Логика определения дублей при импорте — тоже Этап 9.

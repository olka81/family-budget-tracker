# Как устроен FamilyBudget: обучающий разбор архитектуры

Этот документ — не каталог файлов и не пересказ `CLAUDE.md`. Цель другая: после
быстрой итеративной разработки с большим количеством менторских разговоров
понять **почему** проект устроен именно так, а не просто **что** в нём есть.
Все технические детали ниже сверены с реальным кодом на диске (`Models/`,
`Data/`, `Program.cs`, `Areas/Identity/`, `Migrations/`), а не только с
документацией — в этом проекте уже бывали случаи, когда описание расходилось
с фактическим состоянием файлов.

Состояние на момент написания: пройдены Этапы 0–4, Этап 5 (аутентификация)
почти завершён — подробности в разделе 9.

---

## Общая архитектура

Пять технологий в проекте играют разные роли, и важно понимать не абстрактно
"что это такое", а как они физически связаны именно здесь.

**ASP.NET Core** — это хост и точка входа. `Program.cs` — единственное место,
где происходит сборка всего приложения: какие сервисы существуют (DI-контейнер),
в каком порядке обрабатывается HTTP-запрос (middleware pipeline), какие URL на
что отвечают (маршрутизация). Всё остальное — Blazor, EF Core, Identity —
не самостоятельные программы, а *сервисы*, зарегистрированные внутри этого
одного ASP.NET Core приложения.

**Blazor Server** — это UI-часть, и именно она определяет, как работает
большинство страниц. Важно понимать разницу с "обычным" вебом: обычно браузер
делает HTTP-запрос → сервер отвечает HTML → страница отрисована, и на этом
всё. У Blazor Server это выглядит иначе: после первой загрузки страницы
браузер открывает постоянное соединение по WebSocket (через SignalR) —
"circuit" — и дальше сервер и браузер обмениваются не HTML-страницами, а
маленькими сообщениями ("нажали кнопку" / "обнови вот этот кусок DOM").
Реальный C#-код всё это время выполняется на сервере; в браузер уходит только
разница в интерфейсе. Отсюда и выбор в `CLAUDE.md`: Blazor Server выбран
осознанно вместо отдельного JS-фреймворка именно потому, что цель проекта —
прокачать бэкенд-навыки, а не учить ещё один клиентский стек.

**EF Core** — это переводчик между C#-объектами (`Purchase`, `Product` и т.д.)
и таблицами Postgres. Он не хранит данные сам — он единственный, кто *умеет
разговаривать* с базой на SQL от имени C#-кода. Подробно про механику этого
перевода — в разделе 3.

**PostgreSQL** — сама база данных, физически разная в зависимости от
окружения: локально это Postgres в Docker-контейнере, в проде — управляемый
сервис Neon (подробности в разделе 8). Важный момент: C#-код (`Program.cs`,
`FamilyBudgetDbContext`) вообще не знает и не должен знать, с какой именно
базой он говорит — он просто получает строку подключения из конфигурации и
использует провайдер `Npgsql.EntityFrameworkCore.PostgreSQL`, который умеет
разговаривать с любым Postgres, откуда бы он ни был поднят.

**ASP.NET Core Identity** — не отдельная база и не отдельный сервис, а
надстройка поверх EF Core. У неё есть собственные таблицы
(`AspNetUsers`, `AspNetRoles`, `AspNetUserClaims`, `AspNetUserRoles`,
`AspNetUserLogins`, `AspNetUserTokens`), но они лежат в **той же** базе и
через **тот же** `FamilyBudgetDbContext`, что и остальные таблицы проекта —
это не отдельная подсистема, а просто ещё один набор EF Core сущностей.
Physически это работает благодаря тому, что `FamilyBudgetDbContext`
наследуется не от обычного `DbContext`, а от
`IdentityDbContext<ApplicationUser, IdentityRole<int>, int>` — базовый класс
Identity, который сам добавляет эти шесть таблиц в модель EF Core.

Как всё это физически связывается в `Program.cs`, в порядке регистрации:

1. `AddRazorComponents().AddInteractiveServerComponents()` — включает Blazor
   Server pipeline для основного приложения.
2. `AddRazorPages()` — включает *классический* Razor Pages pipeline, который
   в этом проекте используется только для трёх страниц Identity
   (`Login`/`Register`/`Logout`) — почему они не Blazor-компоненты, подробно
   разобрано в разделе 6.
3. `AddDbContextFactory<FamilyBudgetDbContext>(...)` — регистрирует доступ к
   базе (почему именно `Factory`, а не обычный `AddDbContext` — раздел 5).
4. `AddIdentity<ApplicationUser, IdentityRole<int>>(...)
   .AddEntityFrameworkStores<FamilyBudgetDbContext>()
   .AddDefaultTokenProviders()` — включает Identity и явно указывает, что
   хранить её данные нужно через `FamilyBudgetDbContext` (раздел 6).
5. `AddCascadingAuthenticationState()` — делает информацию "кто вошёл в
   систему" доступной Blazor-компонентам ниже по дереву (пока без
   потребителей — см. раздел 9).

А порядок middleware (`UseAuthentication()` → `UseAuthorization()` →
`UseAntiforgery()` → маршруты) — это не случайный список, а последовательность,
в которой каждый шаг должен успеть отработать до следующего: сначала нужно
понять, кто отправил запрос (`Authentication`), потом — можно ли ему то, что
он просит (`Authorization`), и только потом обрабатывать сам запрос.

---

## Классы-сущности

Ниже — каждый класс из `Models/`, что он значит и почему он спроектирован
именно так, а не иначе.

### `FamilyGroup`

```csharp
public class FamilyGroup
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
```

Самая простая сущность в проекте, и при этом — корень всей модели
многопользовательской изоляции (multi-tenancy). Идея: `Category`, `Product`,
`Store`, `Purchase` — не общие для всего приложения, а свои у каждой семьи.
Практически это значит, что почти у каждой другой таблицы есть
`FamilyGroupId`, и вся выборка данных всегда идёт "в рамках своей группы".

Важное архитектурное решение, зафиксированное в `CLAUDE.md`: **у каждого
пользователя ровно одна группа**, и не бывает состояния "без группы" —
пользователь-одиночка получает автоматически созданную "группу из одного
человека". Это решение убирает из всего остального кода необходимость
проверять null-группу как отдельный случай — `FamilyGroupId` у
`ApplicationUser` не nullable именно поэтому.

### `ApplicationUser : IdentityUser<int>`

```csharp
public class ApplicationUser : IdentityUser<int>
{
    public string DisplayName { get; set; } = string.Empty;
    public int FamilyGroupId { get; set; }
    public FamilyGroup FamilyGroup { get; set; } = null!;
}
```

Почему наследование от `IdentityUser<int>`, а не самодельный класс
(как было на Этапе 3, до появления Identity — самодельные `Email`,
`PasswordHash`, `GoogleId`)? Потому что `IdentityUser<TKey>` уже даёт готовые,
проверенные вещи, которые крайне не хочется писать своими руками в 2026 году:
безопасное хеширование пароля, "security stamp" (механизм, который делает все
чужие сессии недействительными при смене пароля), блокировку после
неудачных попыток входа, и — что здесь особенно важно — таблицу
`AspNetUserLogins` для внешних логинов. Раньше "вход через Google" был бы
самодельным полем `GoogleId`; теперь это штатный сценарий Identity, просто
ещё одна строка в `UserLogins` — задел на Google OAuth, который по плану
идёт следующим подшагом Этапа 5.

Дженерик-параметр `<int>` — не значение по умолчанию: базовый
`IdentityUser` без параметра использует `string` (обычно GUID) как тип
первичного ключа. Здесь явно выбран `int`, чтобы совпадать по типу со всеми
остальными первичными ключами в проекте (`FamilyGroup.Id`, `Category.Id` —
тоже `int`) и чтобы внешние ключи на пользователя (`Purchase.UserId`)
оставались простыми int-колонками, а не строками.

К базовому классу добавлены два кастомных поля: `DisplayName` (то, что
реально видно в интерфейсе — email не всегда удобное отображаемое имя) и
`FamilyGroupId`/`FamilyGroup` — связь с группой, обязательная (не nullable),
согласно решению "ровно одна группа на пользователя".

### `Category`

```csharp
public class Category
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int? ParentCategoryId { get; set; }
    public Category? ParentCategory { get; set; }
    public ICollection<Category> ChildCategories { get; set; } = new List<Category>();
    public int FamilyGroupId { get; set; }
    public FamilyGroup FamilyGroup { get; set; } = null!;
}
```

Самоссылка (`ParentCategoryId` — внешний ключ на саму же таблицу
`Category`) — это способ выразить иерархию произвольной глубины
("Продукты" → "Молочное" → "Сыры" → …) одной и той же таблицей, без
ограничения на количество уровней. Альтернатива — завести
фиксированные колонки `Level1Id`, `Level2Id`, `Level3Id`… — работала бы, но
жёстко ограничивала бы глубину вложенности числом колонок; самоссылка снимает
это ограничение полностью.

Важный нюанс, зафиксированный и в `DB_SCHEMA_semeynyi_budget.md`, и в
`CLAUDE.md`: защита от зацикливания иерархии (A является родителем B, B —
родителем A) **не может быть выражена обычным SQL CHECK-ограничением** для
произвольной глубины вложенности — проверку "нет ли предка среди потомков"
придётся писать в коде приложения при каждом изменении `ParentCategoryId`.
Пока управление категориями не реализовано (Этап 6 не начат), эта проверка
просто ещё не написана — это не забытый баг, а часть, которая появится вместе
с самим функционалом категорий.

### `Product`

```csharp
public class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int? CategoryId { get; set; }
    public Category? Category { get; set; }
    public int FamilyGroupId { get; set; }
    public FamilyGroup FamilyGroup { get; set; } = null!;
}
```

Здесь стоит задержаться подробнее — это, пожалуй, самое нетривиальное
решение в модели, и оно прямо названо в `CLAUDE.md` как ключевое: **цена не
хранится в `Product`**. `Product` — это просто название (плюс необязательная
категория), то есть запись каталога, а не факта покупки.

Почему? Один и тот же товар ("Молоко") покупается снова и снова, и цена на
него со временем меняется. Если бы `Price` жило в `Product`, при каждом
изменении цены пришлось бы либо переписывать цену прошлых покупок задним
числом (искажая историю трат), либо заводить новую запись `Product` под
каждую новую цену — что превращает "Молоко" в десятки почти одинаковых строк
и ломает саму идею отчёта "сколько всего потрачено на Молоко за год".
Поэтому цена (и количество, и дата) — это свойства конкретной покупки
(`Purchase`), а `Product` остаётся стабильной точкой отсчёта.

Уникальность `Product.Name` в рамках `FamilyGroupId` (составной уникальный
индекс `(FamilyGroupId, Name)`) добавлена отдельной миграцией уже *после*
первоначальной схемы — специально ради предстоящего сценария Этапа 6:
"найти продукт по названию, если не найден — создать". Без уникального
индекса это классический race condition: два одновременных запроса оба
успевают проверить "такого продукта ещё нет" **до** того, как любой из них
успевает его создать, и оба создают дубликат. Проверка "есть ли уже такой" в
коде приложения эту гонку не закрывает — только ограничение на уровне самой
базы данных гарантированно её исключает. Практическое следствие: код, который
создаёт `Product`, должен быть готов поймать исключение о нарушении
уникального ограничения и в этом случае просто заново прочитать уже
созданную кем-то другим строку, а не считать, что вставка обязана пройти
успешно.

**Расхождение с концептуальной схемой, найденное при сверке с кодом**:
`DB_SCHEMA_semeynyi_budget.md` описывает `PaymentMethod` (в `Purchase`,
см. ниже) как `smallint`. В реальности EF Core по умолчанию хранит C#-enum
как обычный 4-байтовый `integer` — ни в `OnModelCreating`, ни где-либо ещё
нет явного указания хранить его как `smallint`, и миграция
`InitialCreate.cs` подтверждает: колонка объявлена как
`table.Column<int>(type: "integer", ...)`. Для enum с двумя значениями
(`Card`/`Cash`) разница в 2 байта на строку не имеет практического значения,
но это хороший пример того, зачем сверять код с документацией напрямую —
концептуальная схема фиксировала намерение, а конкретный тип колонки решился
уже конвенцией EF Core на Этапе 3, и это нигде явно не задокументировано.

### `Store`

```csharp
public class Store
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int FamilyGroupId { get; set; }
    public FamilyGroup FamilyGroup { get; set; } = null!;
}
```

Самая простая из привязанных к группе сущностей — просто название магазина.
Связь с `Purchase` необязательна (`StoreId` там nullable) — не у каждой
траты есть физический магазин (например, оплата услуги или денежный подарок).

### `Purchase`

```csharp
public class Purchase
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public decimal Price { get; set; }
    public decimal Quantity { get; set; } = 1;
    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Card;
    public DateOnly Date { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow);
    public int? StoreId { get; set; }
    public Store? Store { get; set; }
    public int UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;
    public int FamilyGroupId { get; set; }
    public FamilyGroup FamilyGroup { get; set; } = null!;
    public string Currency { get; set; } = "EUR";
    public string? Notes { get; set; }
}
```

Центральная, самая "тяжёлая" по количеству связей сущность — это факт
конкретной покупки. Несколько деталей, которые не случайны:

- `Price`/`Quantity` — `decimal`, никогда не `float`/`double` (зафиксировано
  в `CLAUDE.md` как жёсткое правило) — деньги нельзя хранить в типах с
  плавающей точкой из-за неточности округления, которая для денег
  недопустима.
- `Date` — `DateOnly`, а не `DateTime`: у даты покупки нет осмысленного
  времени суток, и `DateOnly` убирает саму возможность путаницы с часовыми
  поясами, которая обязательно возникла бы при использовании `DateTime`.
- `UserId` обязателен (не nullable) — по решению "без абстрактных
  покупателей": у покупки всегда есть конкретный автор записи, и по
  умолчанию это тот, кто её вносит. Но, как и с категориями, обычный внешний
  ключ не может проверить, что этот пользователь состоит именно в той же
  `FamilyGroup`, что и сама покупка — эта проверка тоже должна быть на
  уровне кода при сохранении (пока не реализована, т.к. форма добавления
  покупки ещё не написана).
- `FamilyGroupId` на `Purchase` — на первый взгляд избыточное поле:
  группу можно было бы получить и через `Product.FamilyGroup`, и через
  `User.FamilyGroup`. Но `DB_SCHEMA_semeynyi_budget.md` прямо называет это
  дублирование намеренным: оно "упрощает запросы для отчётов" — отчёт вида
  "все траты группы X за период" можно сделать одним условием
  `WHERE FamilyGroupId = X` без обязательного join через `Product` или
  `User`. Это классический сознательный компромисс "немного денормализации
  ради более простых и быстрых запросов на чтение", а не недосмотр.
- `Currency` — явная колонка, хотя MVP использует только EUR. Это задел на
  будущее: когда мультивалютность понадобится, не придётся добавлять новую
  колонку и разбираться, что означает её отсутствие для старых записей.

### `PaymentMethod`

```csharp
public enum PaymentMethod
{
    Card,
    Cash
}
```

Простой C#-enum. Ничего архитектурно сложного, но именно на его примере
выше показано расхождение между концептуальной схемой (`smallint`) и
фактическим хранением (`integer`) — конвенция EF Core "просто сработала"
по умолчанию, и никто отдельно не решал сузить тип колонки.

---

### Диаграмма классов

_Диаграмма классов_ — навигационные свойства между сущностями (визуально
она должна зеркалить ER-диаграмму ниже, но это C#-объекты со свойствами, а
не таблицы со столбцами):

```mermaid
classDiagram
    class FamilyGroup {
        +int Id
        +string Name
        +DateTime CreatedAt
    }
    class ApplicationUser {
        +int Id
        +string Email
        +string UserName
        +string? PasswordHash
        +string DisplayName
        +int FamilyGroupId
        +FamilyGroup FamilyGroup
    }
    class Category {
        +int Id
        +string Name
        +int? ParentCategoryId
        +Category? ParentCategory
        +ICollection~Category~ ChildCategories
        +int FamilyGroupId
        +FamilyGroup FamilyGroup
    }
    class Product {
        +int Id
        +string Name
        +int? CategoryId
        +Category? Category
        +int FamilyGroupId
        +FamilyGroup FamilyGroup
    }
    class Store {
        +int Id
        +string Name
        +int FamilyGroupId
        +FamilyGroup FamilyGroup
    }
    class Purchase {
        +int Id
        +int ProductId
        +Product Product
        +decimal Price
        +decimal Quantity
        +PaymentMethod PaymentMethod
        +DateOnly Date
        +int? StoreId
        +Store? Store
        +int UserId
        +ApplicationUser User
        +int FamilyGroupId
        +FamilyGroup FamilyGroup
        +string Currency
        +string? Notes
    }
    class PaymentMethod {
        <<enumeration>>
        Card
        Cash
    }

    FamilyGroup "1" --> "many" ApplicationUser : FamilyGroup
    FamilyGroup "1" --> "many" Category : FamilyGroup
    FamilyGroup "1" --> "many" Product : FamilyGroup
    FamilyGroup "1" --> "many" Store : FamilyGroup
    FamilyGroup "1" --> "many" Purchase : FamilyGroup
    Category "0..1" --> "many" Category : ParentCategory / ChildCategories
    Category "0..1" --> "many" Product : Category
    Product "1" --> "many" Purchase : Product
    Store "0..1" --> "many" Purchase : Store
    ApplicationUser "1" --> "many" Purchase : User
    Purchase --> PaymentMethod
```

*Диаграмма классов.*

---

## Как C#-классы превращаются в таблицы базы данных

`FamilyBudgetDbContext` — это единственная точка, через которую C#-код
вообще разговаривает с базой. `DbSet<T>`-свойства на нём
(`FamilyGroups`, `Categories`, `Products`, `Stores`, `Purchases`) — это не
"списки в памяти", а точки входа для запросов к соответствующим таблицам:
`context.Purchases.Where(...)` превращается EF Core в реальный SQL-запрос
только в момент, когда результат действительно понадобился (например, при
вызове `.ToList()` или переборе в `foreach`).

`OnModelCreating` выполняется один раз при старте — это момент, когда EF
Core строит внутреннюю "модель" (метаданные: какие таблицы, столбцы, ключи,
связи должны существовать), ещё *до* какого-либо реального обращения к базе.
Многое EF Core выводит по соглашению автоматически: свойство `Id` — это
первичный ключ, пара `CategoryId` + навигационное свойство `Category` — это
внешний ключ. Но там, где по одним только именам свойств однозначно не
понять, что имелось в виду, или где конвенция сделала бы не то, что нужно,
приходится поправлять её явно через **Fluent API** — методы вроде `HasOne`,
`WithMany`, `HasForeignKey`, `OnDelete`, `HasIndex`, вызываемые внутри
`OnModelCreating`. Разберём каждый вызов из реального
`FamilyBudgetDbContext.cs`:

```csharp
modelBuilder.Entity<Category>()
    .HasOne(c => c.ParentCategory)
    .WithMany(c => c.ChildCategories)
    .HasForeignKey(c => c.ParentCategoryId)
    .OnDelete(DeleteBehavior.Restrict);
```

`HasOne(c => c.ParentCategory)` — "у одной `Category` есть один
`ParentCategory`". `WithMany(c => c.ChildCategories)` — "а у того родителя
есть коллекция детей `ChildCategories`". Вместе это описывает
самоссылающуюся связь "один ко многим". `HasForeignKey` здесь обязателен —
без него EF Core не смог бы однозначно понять, какая именно колонка хранит
эту связь, ведь у самоссылки в принципе может быть два направления.
`OnDelete(Restrict)` — "не разрешать удалить категорию, если на неё
ссылаются другие категории как на родителя" — защищает от случайного
разрушения целой ветки иерархии при удалении одного узла где-то в середине.

```csharp
modelBuilder.Entity<Product>()
    .HasOne(p => p.Category)
    .WithMany()
    .HasForeignKey(p => p.CategoryId)
    .OnDelete(DeleteBehavior.SetNull);
```

`WithMany()` без аргумента — "у `Category` не нужна обратная коллекция
`Products`" (связь односторонняя, в отличие от самоссылки категорий выше).
`OnDelete(SetNull)` — раз `CategoryId` у продукта и так nullable, при
удалении категории продукты не удаляются и не блокируют удаление, а просто
теряют категорию (становятся "без категории", что для `Product` — валидное
состояние). Точно та же логика и с `Purchase.Store` — тоже nullable, тоже
`SetNull`.

```csharp
modelBuilder.Entity<Purchase>()
    .HasOne(p => p.Product)
    .WithMany()
    .HasForeignKey(p => p.ProductId)
    .OnDelete(DeleteBehavior.Restrict);

modelBuilder.Entity<Purchase>()
    .HasOne(p => p.User)
    .WithMany()
    .HasForeignKey(p => p.UserId)
    .OnDelete(DeleteBehavior.Restrict);
```

Здесь `NULL` не вариант — у покупки `ProductId` и `UserId` не nullable,
значит при удалении продукта или пользователя базе нужно либо каскадно
снести все связанные покупки, либо отказать в удалении. Выбран отказ
(`Restrict`) — сознательно, для финансового приложения: молча стереть
историю трат из-за удаления продукта или пользователя было бы не
особенностью, а багом.

Остальные внешние ключи (`FamilyGroupId` у `ApplicationUser`, `Category`,
`Product`, `Store`, `Purchase`) нигде явно не настроены — они оставлены на
конвенции EF Core по умолчанию, которая для обязательной (not null) связи
разрешается в `Cascade` (это подтверждено самой миграцией). Здесь это
осмысленное поведение: если удалить `FamilyGroup`, логично, что все данные
этой семьи должны исчезнуть вместе с ней.

```csharp
modelBuilder.Entity<Product>()
    .HasIndex(p => new { p.FamilyGroupId, p.Name })
    .IsUnique();
```

Составной уникальный индекс — по паре колонок сразу, а не по одной. Он
гарантирует (силами самого Postgres, не кода приложения), что *в рамках
одной группы* не может быть двух `Product` с одинаковым именем — но две
разные семьи вполне могут независимо друг от друга иметь каждая свой
"Молоко". Именно это и стоит за фразой "уникальность в рамках FamilyGroupId"
в `DB_SCHEMA_semeynyi_budget.md`.

Наконец, `FamilyBudgetDbContext` наследуется от
`IdentityDbContext<ApplicationUser, IdentityRole<int>, int>`, и
`OnModelCreating` первым делом вызывает `base.OnModelCreating(modelBuilder)`
— именно этот вызов добавляет в модель шесть таблиц Identity
(`AspNetUsers` и остальные). Если бы этот вызов был пропущен или вызван
после кастомной конфигурации в конфликтующем порядке, таблицы Identity либо
не появились бы вовсе, либо появились бы без части связей.

---

### ER-диаграмма

```mermaid
erDiagram
    FamilyGroup ||--o{ ApplicationUser : "содержит"
    FamilyGroup ||--o{ Category : "содержит"
    FamilyGroup ||--o{ Product : "содержит"
    FamilyGroup ||--o{ Store : "содержит"
    FamilyGroup ||--o{ Purchase : "содержит"
    Category ||--o{ Category : "родитель / дети"
    Category |o--o{ Product : "категория (nullable)"
    Product ||--o{ Purchase : "товар"
    Store |o--o{ Purchase : "магазин (nullable)"
    ApplicationUser ||--o{ Purchase : "автор записи"

    FamilyGroup {
        int Id PK
        string Name
        timestamp CreatedAt
    }
    ApplicationUser {
        int Id PK
        string Email
        string UserName
        string PasswordHash "nullable"
        string DisplayName
        int FamilyGroupId FK "not null"
    }
    Category {
        int Id PK
        string Name
        int ParentCategoryId FK "nullable, самоссылка"
        int FamilyGroupId FK "not null"
    }
    Product {
        int Id PK
        string Name "уникально вместе с FamilyGroupId"
        int CategoryId FK "nullable"
        int FamilyGroupId FK "not null, уник.индекс с Name"
    }
    Store {
        int Id PK
        string Name
        int FamilyGroupId FK "not null"
    }
    Purchase {
        int Id PK
        int ProductId FK "not null"
        numeric Price "not null"
        numeric Quantity "not null, default 1"
        int PaymentMethod "enum Card/Cash, not null"
        date Date "not null, default сегодня"
        int StoreId FK "nullable"
        int UserId FK "not null"
        int FamilyGroupId FK "not null, дублируется намеренно"
        string Currency "not null, default EUR"
        string Notes "nullable"
    }
```

*Схема базы данных.*

---

## Миграции

Миграция EF Core — это способ довести реальную структуру уже существующей
базы данных до состояния, которое описывает текущая C#-модель, шаг за шагом,
предсказуемо и обратимо (у каждой миграции есть `Up()` и `Down()`).

Команда `dotnet ef migrations add <Имя>` сравнивает **текущую модель**
(то, что построил бы `OnModelCreating` прямо сейчас) с **сохранённым
снимком модели** — файлом `FamilyBudgetDbContextModelSnapshot.cs`, который
описывает, какой была модель после применения всех миграций до этой. Разница
между "сейчас" и "снимком" превращается в новый файл миграции с конкретными
вызовами `MigrationBuilder` (`CreateTable`, `CreateIndex`, `DropIndex`,
`AddColumn` и т.д.) — и в парную `Down()`, отменяющую этот же набор
изменений.

Наглядный пример из проекта — вся вторая миграция,
`AddProductNameUniqueIndex.cs`, целиком:

```csharp
protected override void Up(MigrationBuilder migrationBuilder)
{
    migrationBuilder.DropIndex(name: "IX_Products_FamilyGroupId", table: "Products");
    migrationBuilder.CreateIndex(
        name: "IX_Products_FamilyGroupId_Name",
        table: "Products",
        columns: new[] { "FamilyGroupId", "Name" },
        unique: true);
}
```

Это именно та ситуация, которую `NOTES.md` описывает как "нормально, не
ошибка": когда добавляешь составной уникальный индекс по полю, у которого
уже был одиночный auto-индекс (тут — по внешнему ключу `FamilyGroupId`), EF
Core сам генерирует миграцию, которая сначала сносит старый одиночный индекс,
а затем создаёт новый составной — он и так покрывает поиск по одному
`FamilyGroupId`, старый становится избыточным.

`FamilyBudgetDbContextModelSnapshot.cs` — не применяется к базе напрямую,
это исключительно собственная "бухгалтерия" EF Core: полное C#-описание
того, как выглядит модель после всех применённых миграций, нужное для того,
чтобы следующий `migrations add` было с чем сравнивать, не подключаясь к
реальной базе и не реконструируя её схему заново. Именно поэтому этот файл
не редактируют руками — если он разойдётся с фактической суммой всех
миграций, следующее сравнение станет неверным.

Отдельный `FamilyBudgetDbContextFactory.cs`
(`IDesignTimeDbContextFactory<FamilyBudgetDbContext>`) существует по
конкретной технической причине: команда `dotnet ef` должна суметь создать
экземпляр `FamilyBudgetDbContext`, чтобы посмотреть на его модель — но она
делает это полностью в обход обычного запуска приложения: `Program.cs` не
выполняется, DI-контейнер не собран, `builder.Configuration` не построен.
Поэтому инструментарий EF Core ищет в проекте класс, реализующий
`IDesignTimeDbContextFactory<T>`, и если находит — вызывает его
`CreateDbContext(args)`, чтобы получить контекст *своим собственным* путём,
никак не связанным с тем, как контекст собирается в реальном приложении
через `AddDbContextFactory` в `Program.cs`. Именно поэтому фабрике приходится
самостоятельно собирать `ConfigurationBuilder`, читающий
`appsettings.Development.json` и переменные окружения — при разработке
`Program.cs` попросту никогда не выполняется. А поскольку переменные
окружения добавлены *после* файла (и поэтому перекрывают его), можно
временно указать миграции работать с Neon вместо локальной базы, задав
`ConnectionStrings__DefaultConnection` прямо в терминале перед командой —
эта переменная будет видна только фабрике конкретно во время выполнения
`dotnet ef`-команды в этом окне терминала, и никак не повлияет на обычный
локальный запуск приложения.

---

## AddDbContextFactory против AddDbContext

Это решение (Architecture Decision #7 в `CLAUDE.md`) — прямое следствие
того, как Blazor Server работает физически (см. раздел 1). При обычном
`AddDbContext<T>()` DI-контейнер выдаёт **один экземпляр** контекста на
каждый "scope" — и в классическом вебе (Razor Pages, MVC, Web API) scope
живёт ровно один HTTP-запрос: контекст создаётся, используется, уничтожается
— и так на каждый запрос заново. Всё безопасно.

В Blazor Server всё иначе: как только у пользователя открывается "circuit"
(постоянное SignalR-соединение, пока открыта вкладка браузера), DI-scope для
этого circuit живёт **всё то время, пока открыт circuit** — а не на каждый
запрос. Это может быть часы. Если бы Blazor-компонент напрямую внедрял
(injected) `DbContext`, это был бы **тот же самый** экземпляр на протяжении
всей сессии — а внутренний "трекер изменений" EF Core держит ссылки на
каждую загруженную сущность, пока сам контекст не будет уничтожен. Если этого
не происходит часами — память постепенно растёт (это документированная,
реальная проблема именно Blazor Server, а не выдуманная предосторожность).
Плюс отдельная проблема: `DbContext` не потокобезопасен, а несколько
рендеров/компонентов вполне могут дёрнуть его параллельно.

`IDbContextFactory<T>` решает это иначе: компонент внедряет не сам контекст,
а **фабрику контекстов** — `IDbContextFactory<FamilyBudgetDbContext>` —
которая сама по себе ничего не отслеживает и потому безопасно живёт весь
circuit. А каждый раз, когда нужно реально сходить в базу, вызывается:

```csharp
using var context = DbFactory.CreateDbContext();
// ...операция с базой...
```

— короткоживущий контекст ровно на одну операцию, который `using`
уничтожает сразу же после использования, освобождая всё, что он успел
отследить, а не копя это часами. Именно это правило и зафиксировано в
`CLAUDE.md`: "никогда не внедрять `FamilyBudgetDbContext` напрямую в
Blazor-компонент".

Практика в текущем коде: пока ни один Blazor-компонент к базе вообще не
обращается (Этап 6 не начат) — регистрация фабрики в `Program.cs` готова и
ждёт, когда появится первый такой код. А `RegisterModel` (это Razor Page, не
Blazor-компонент!) законно внедряет `FamilyBudgetDbContext` **напрямую**, а
не через фабрику — потому что у Razor Page действительно нет проблемы
долгоживущего circuit: её scope и правда равен одному HTTP-запросу, так что
обычный контекст на запрос там абсолютно корректен. `CLAUDE.md` отдельно
это подчёркивает как осознанное, а не ошибочное решение.

---

## Аутентификация: ASP.NET Core Identity

`AddIdentity<ApplicationUser, IdentityRole<int>>(...)` регистрирует базовые
сервисы Identity — `UserManager<TUser>`, `SignInManager<TUser>`, хеширование
паролей и всё остальное — но **не** говорит, где физически хранить данные, и
**не** подключает никакой готовый интерфейс. `.AddEntityFrameworkStores<FamilyBudgetDbContext>()`
— вот что подключает EF Core как механизм хранения: он регистрирует
реализации `UserStore`/`RoleStore`, которые работают через указанный
контекст. Именно поэтому `FamilyBudgetDbContext` вообще пришлось сделать
`IdentityDbContext<...>` — EF-реализация хранилища Identity ожидает, что
контекст уже содержит нужные шесть `DbSet`, и `IdentityDbContext<ApplicationUser,
IdentityRole<int>, int>` — как раз тот базовый класс, который их
предоставляет. `.AddDefaultTokenProviders()` регистрирует механизмы генерации
временных токенов (для подтверждения email, сброса пароля) — они не
используются активно прямо сейчас (`RequireConfirmedAccount = false`,
страницы `ForgotPassword` нет), но регистрируются как безобидная база на
будущее.

**Почему не `AddDefaultIdentity`.** `AddDefaultIdentity<TUser>()` — это
готовая обёртка Microsoft для сценария "дайте просто рабочий Identity UI
побыстрее": она тащит с собой **свой** набор Razor Pages (те же, что
получились бы из `dotnet new blazor -au Individual` без доработок) и по
умолчанию рассчитана на `IdentityRole<string>` — она не даёт явно указать
`IdentityRole<int>`, потому что спроектирована так, чтобы про это вообще не
думать. В этом проекте свои страницы Login/Register/Logout уже
заскаффолжены отдельно, через `dotnet aspnet-codegenerator identity`,
именно ради кастомизации `Register` (создание `FamilyGroup`) — если бы
одновременно был активен встроенный UI от `AddDefaultIdentity` **и**
собственные страницы под теми же маршрутами, это был бы конфликт двух
реализаций одной и той же страницы. Поэтому используется низкоуровневый
`AddIdentity<ApplicationUser, IdentityRole<int>>()`, который регистрирует
только сервисы и оставляет весь UI полностью в руках заскаффолженных
страниц.

**Почему Login/Register/Logout — классические Razor Pages, а не
Blazor-компоненты.** Дело в самом механизме HTTP: заголовки ответа (включая
`Set-Cookie`, которым Identity выставляет cookie аутентификации после
успешного входа) можно отправить браузеру **только один раз** и только до
того, как начала уходить хоть какая-то часть тела ответа. Классический
запрос к Razor Page работает как обычный веб-запрос "по старинке": сервер
сначала полностью выполняет весь код обработчика (`OnPostAsync`, внутри
которого `SignInManager.SignInAsync` дописывает cookie в
`HttpContext.Response`), и только *после* этого ASP.NET Core одним пакетом
отправляет готовый ответ (заголовки + тело) обратно по HTTP-соединению — то
есть есть чёткий момент "до отправки", когда cookie ещё можно дописать.

Интерактивный Blazor Server circuit устроен принципиально иначе: начальная
загрузка страницы действительно была обычным HTTP-ответом с заголовками —
но как только эта страница "ожила" и открылось SignalR-соединение, любое
дальнейшее взаимодействие (клик на кнопку, `OnClick` и т.п.) идёт **не**
через новый HTTP-запрос/ответ, а через сообщение по уже открытому
соединению, заголовки которого давно отправлены. Отправлять новый
`Set-Cookie` внутри уже живого circuit попросту некуда — исходный HTTP-ответ
для этой страницы был отправлен целиком ещё в момент её первой загрузки.
Поэтому кнопка внутри интерактивного Blazor-компонента физически не может
выставить cookie аутентификации так, чтобы браузер её увидел — сам механизм
для этого отсутствует. Отсюда решение: три страницы Identity остаются
классическими (не интерактивными) Razor Pages, где выставление cookie
происходит в рамках одного обычного цикла запрос/ответ, а остальное
приложение свободно остаётся полноценным интерактивным Blazor Server.

**Отдельная разметка (`_Layout`/`_LoginPartial`) для страниц Identity.**
`Areas/Identity/Pages/_ViewStart.cshtml` задаёт `Layout = "_Layout";` для
всех страниц под `Areas/Identity/Pages`, и это **собственный**, отдельный от
основного приложения Razor-макет — `Areas/Identity/Pages/Shared/_Layout.cshtml`
— а вовсе не `Components/Layout/MainLayout.razor` (это Blazor-макет, из
совершенно другой системы рендеринга). Инструмент скаффолдинга по умолчанию
пишет `Layout = "/Pages/Shared/_Layout.cshtml"` — путь, рассчитанный на
классический проект Razor Pages со своей корневой папкой `Pages/Shared`,
которой в этом Blazor Web App попросту нет; это пришлось поправить на
короткое имя `"_Layout"` и создать собственный файл макета именно в
`Areas/Identity/Pages/Shared/`. Внутри него `_LoginPartial` (в той же папке)
подключается способом, характерным для классических Razor Pages —
`Engine.FindView(...)` + `Html.RenderPartialAsync(...)` — и показывает
"Hello, {email}!" с формой выхода, если пользователь вошёл, либо ссылки
Register/Login, если нет.

Важный нюанс, который стоит держать в голове: эти ссылки видны **только**
на самих страницах Identity. `Components/Layout/NavMenu.razor` в основном
Blazor-приложении — до сих пор нетронутый шаблон (`Home`/`Counter`/
`Weather`), без единой ссылки на вход или выход. Это значит, что прямо
сейчас у пользователя нет способа попасть на страницу входа изнутри
приложения — только вручную набрав URL. Это осознанно зафиксированный,
пока не решённый пробел (см. раздел 9 и открытый вопрос №3 в
`CODE_REVIEW.md`), а не забытая деталь.

Ещё одна известная, отложенная мелочь: макет ссылается на пути вида
`~/Identity/lib/bootstrap/...`, которых в `wwwroot/` не существует (в
`wwwroot` вообще нет папки `Identity/`) — из-за этого формы Identity
отображаются без стилей, а заодно (это уже не задокументированная ранее
деталь, найденная при сверке файлов) `_ValidationScriptsPartial.cshtml`
ссылается на `~/lib/jquery-validation/...`, которого тоже нет — то есть
клиентская валидация форм сейчас не работает вовсе, есть только серверная
проверка при отправке формы.

---

## Сквозной сценарий регистрации

Разберём `Register.cshtml.cs` → `OnPostAsync()` построчно — это самый
насыщенный решениями участок кода в проекте на сегодняшний день.

```csharp
var user = CreateUser();

await _userStore.SetUserNameAsync(user, Input.Email, CancellationToken.None);
await _emailStore.SetEmailAsync(user, Input.Email, CancellationToken.None);
```

Зачем нужны именно эти вызовы, а не просто `user.Email = Input.Email`?
Identity внутренне разделяет понятия `UserName` и `Email` — даже если в этом
приложении визуально они совпадают, само хранилище Identity ожидает, что оба
значения будут выставлены через его собственные абстракции (`IUserStore`/
`IUserEmailStore`), а не напрямую через C#-свойства, потому что за этими
вызовами скрывается дополнительная бухгалтерия — например, заполнение
`NormalizedUserName`/`NormalizedEmail`, которые используются для поиска без
учёта регистра. Если пропустить эти вызовы (или вызвать их в неверном
порядке — до `CreateUser()`, а не после), `UserName` останется пустым, а
Identity требует непустой `UserName` — это именно тот баг, который прямо
назван в `CLAUDE.md`: ошибка `"Username '' is invalid"`.

```csharp
using var transaction = await _familyBudgetDbContext.Database.BeginTransactionAsync();

var familyGroup = new FamilyGroup { Name = "My Family" };
_familyBudgetDbContext.FamilyGroups.Add(familyGroup);
await _familyBudgetDbContext.SaveChangesAsync();

user.FamilyGroupId = familyGroup.Id;

var result = await _userManager.CreateAsync(user, Input.Password);
```

`FamilyGroup` создаётся и сохраняется (`SaveChangesAsync()`) **раньше**, чем
создаётся пользователь — потому что настоящий, сгенерированный базой `Id`
группы появляется только в момент вставки строки, а без реального `Id`
привязать к нему пользователя (`user.FamilyGroupId = familyGroup.Id`)
невозможно.

Обе вставки — группы и пользователя — обёрнуты одной транзакцией. Зачем?
Реальная история этого проекта прямо отвечает на этот вопрос: если бы
транзакции не было, а `_userManager.CreateAsync` (следующий шаг после
вставки группы) провалился бы — самый вероятный сценарий как раз тот, что
Identity сам проверяет: попытка зарегистрироваться с уже занятым именем
пользователя — строка `FamilyGroup` осталась бы в базе навсегда, никем не
используемая. При каждой неудачной попытке регистрации с уже занятым email
накапливалась бы ещё одна "осиротевшая" группа. Транзакция превращает обе
вставки в одну неделимую операцию:

```csharp
if (result.Succeeded)
{
    await transaction.CommitAsync();
    ...
    await _signInManager.SignInAsync(user, isPersistent: false);
    return LocalRedirect(returnUrl);
}
// иначе — просто добавляем ошибки в ModelState и возвращаем Page()
```

Здесь стоит обратить внимание на тонкость, которую легко упустить:
**нигде нет явного вызова "откатить транзакцию"**. `transaction.CommitAsync()`
вызывается только внутри `if (result.Succeeded)`. Если метод доходит до
`return Page()` без коммита — за откат отвечает `using`: когда область
видимости `transaction` заканчивается, EF Core вызывает у неё `Dispose()`,
а у ещё не закоммиченной транзакции `Dispose()` автоматически выполняет
`ROLLBACK`. Это значит, что уже выполнившаяся ранее строка кода
`await _familyBudgetDbContext.SaveChangesAsync()` (вставка группы) на уровне
базы данных отменяется — несмотря на то, что сама C#-инструкция уже
"отработала" раньше по времени, чем случился сбой. Это и есть разница между
"инструкция выполнилась" и "транзакция закоммичена".

Именно это вручную проверяет тест, описанный в `NOTES.md`: зарегистрироваться
один раз (появляется 1 группа + 1 пользователь) → зарегистрироваться ещё раз
**тем же email** (Identity всегда проверяет уникальность имени пользователя,
независимо от настройки `RequireUniqueEmail`, поэтому вторая попытка
отклоняется) → если транзакция действительно работает, вторая, осиротевшая
группа не появляется, несмотря на то, что вставка группы для второй попытки
успела выполниться до того, как выяснилось, что пользователь создать не
получится.

При успехе — `_signInManager.SignInAsync(user, isPersistent: false)` сразу
выставляет cookie аутентификации (тот самый механизм из раздела 6) и
пользователь считается вошедшим сразу после регистрации, без отдельного
шага входа.

### Диаграмма сценария регистрации

```mermaid
sequenceDiagram
    actor Browser as Браузер
    participant Register as RegisterModel.OnPostAsync
    participant Ctx as FamilyBudgetDbContext
    participant UM as UserManager / UserStore
    participant DB as PostgreSQL

    Browser->>Register: POST /Identity/Account/Register (email, пароль)
    Register->>Register: CreateUser() + SetUserNameAsync / SetEmailAsync
    Register->>Ctx: BeginTransactionAsync()
    Ctx->>DB: BEGIN
    Register->>Ctx: FamilyGroups.Add(group); SaveChangesAsync()
    Ctx->>DB: INSERT INTO "FamilyGroups" ...
    DB-->>Ctx: сгенерированный Id группы
    Register->>Register: user.FamilyGroupId = group.Id
    Register->>UM: CreateAsync(user, пароль)
    UM->>DB: INSERT INTO "AspNetUsers" ... (хеш пароля)

    alt регистрация успешна
        UM-->>Register: result.Succeeded = true
        Register->>Ctx: transaction.CommitAsync()
        Ctx->>DB: COMMIT (группа и пользователь сохранены вместе)
        Register->>Register: SignInManager.SignInAsync(user) — cookie
        Register-->>Browser: 302 Redirect + Set-Cookie
    else регистрация не удалась (например, email уже занят)
        UM-->>Register: result.Succeeded = false, errors
        Register->>Register: ошибки добавлены в ModelState, return Page()
        Note over Register,Ctx: CommitAsync() НЕ вызван — при выходе<br/>из using transaction.Dispose() делает ROLLBACK
        Ctx->>DB: ROLLBACK (INSERT группы тоже отменяется)
        Register-->>Browser: 200 с ошибками формы, без cookie
    end
```

*Сценарий регистрации.*

---

## Локальная и продовая база данных

**Локально** — реальный Postgres 18 в Docker-контейнере:

```bash
docker run --name familybudget-db -e POSTGRES_PASSWORD=devpassword \
  -e POSTGRES_DB=familybudget -p 5432:5432 -d postgres:18
```

Строка подключения к нему лежит прямо в `appsettings.Development.json` и
закоммичена в git — и это осознанно, не недосмотр: пароль `devpassword`
годится только для одноразового локального контейнера, который никто извне
не видит, это не настоящий секрет. ASP.NET Core подхватывает именно этот
файл автоматически только тогда, когда переменная окружения
`ASPNETCORE_ENVIRONMENT` равна `Development` — а это значение по умолчанию
при обычном локальном запуске через `dotnet run`.

**В проде** — Neon, управляемый ("serverless") Postgres, регион Frankfurt
(тот же регион, что у хостинга Render — чтобы минимизировать задержку сети
между приложением и базой). Neon выдаёт строку подключения в формате
`postgresql://user:pass@host/db?sslmode=require`, а провайдеру Npgsql нужен
другой формат (`Host=...;Port=5432;Database=...;Username=...;Password=...;
SSL Mode=Require;Channel Binding=Require`) — переводить приходится вручную.
У Neon для одной и той же базы есть **два разных** хоста подключения под
разные задачи: "direct" (без `-pooler` в имени хоста) — для разовых
административных операций вроде применения миграций, и "pooled"
(с `-pooler`, через встроенный PgBouncer от Neon) — для боевого трафика уже
запущенного приложения, которому нужен эффективный пул соединений. У pooled
подключения дополнительно требуется `No Reset On Close=true` — потому что
режим transaction pooling у PgBouncer переиспользует физические соединения
между разными логическими сессиями так, что обычное поведение Npgsql
"сбрасывать соединение при закрытии" начинает конфликтовать с этим режимом.

**Как две базы не мешают друг другу.** `appsettings.json` (общий файл,
попадающий и в прод) вообще не содержит строки подключения — прод
сознательно не получает её из файла. Вместо этого в панели Render, на
вкладке Environment конкретного сервиса, задана переменная окружения
`ConnectionStrings__DefaultConnection` (двойное подчёркивание — соглашение
ASP.NET Core для отображения плоских имён переменных окружения во вложенные
ключи конфигурации, то есть это ровно то же самое, что `ConnectionStrings:
DefaultConnection`). Стандартный порядок слоёв конфигурации ASP.NET Core
читает переменные окружения *после* `appsettings.json` — и поэтому они его
перекрывают. В результате в проде эта переменная просто подставляется как
результат `GetConnectionString("DefaultConnection")`, вообще не касаясь
никакого файла, а локально — поскольку никто не выставил такую переменную
— используется строка из `appsettings.Development.json`. Одна и та же строка
кода в `Program.cs` (`builder.Configuration.GetConnectionString("DefaultConnection")`)
резолвится в совершенно разные настоящие базы данных в зависимости только от
окружения запуска — без единого `if` в коде на этот счёт.

Отдельно от рантайма приложения работает design-time фабрика (раздел 4):
чтобы разово применить миграцию к Neon вместо локальной базы, нужно перед
командой `dotnet ef database update` задать `ConnectionStrings__DefaultConnection`
как переменную окружения именно в текущем окне терминала (PowerShell:
`$env:ConnectionStrings__DefaultConnection="..."`) — и после этого закрыть
это окно, чтобы переменная случайно не осталась висеть и не подставилась в
обычный локальный запуск.

---

## Что уже сделано и что нет

Подробный план по этапам — в `PLAN_semeynyi_budget.md`; здесь только якорь,
чтобы понимать, к какому моменту относится весь текст выше.

- **Этапы 0–4 (инструменты, Hello World + CI/CD, проектирование схемы БД,
  EF Core модели/миграции, подключение Neon к Render) — завершены.**
- **Этап 5 (аутентификация + семейные группы) — почти завершён.**
  Регистрация → вход → выход подтверждены рабочими end-to-end, включая
  проверку транзакции при создании `FamilyGroup` (раздел 7). Уникальный
  индекс на `Product.Name` добавлен и проверен на обеих базах. Не начаты:
  экраны *присоединения* к уже существующей группе (в отличие от
  авто-создаваемой личной группы) и управления группой, а также Google
  OAuth.
- **Этап 6 (учёт покупок) не начат** — этим объясняется, почему ни один
  Blazor-компонент пока не обращается к базе (раздел 5 — фабрика
  зарегистрирована, но ещё не используется) и почему в навигации основного
  приложения нет ссылки на вход/выход (раздел 6).
- **Этапы 7–10 (отчёты, сквозной прогон на проде, CSV-импорт через Cowork,
  тесты) не начаты.**

Из последнего общего code review, всё ещё актуально и осознанно отложено, а
не забыто: две мёртвые ссылки на `Login.cshtml` (`ForgotPassword`,
`ResendEmailConfirmation` — страниц для них нет); отсутствие какой-либо
Identity-интеграции в основном Blazor-приложении (раздел 6); отсутствие
стилей и клиентской валидации на страницах Identity из-за не тех путей к
`wwwroot`.

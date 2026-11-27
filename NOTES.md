# Полезные команды и заметки

## Запуск
- Запуск локально с HTTPS: `dotnet run --launch-profile https`
- Разово, если браузер ругается на сертификат: `dotnet dev-certs https --trust`

## Docker / локальный Postgres
- Создать локальную БД (только самый первый раз): `docker run --name familybudget-db -e POSTGRES_PASSWORD=devpassword -e POSTGRES_DB=familybudget -p 5432:5432 -d postgres:18`
- Запустить уже созданный контейнер (все последующие разы): `docker start familybudget-db`. Повторный `docker run` даст ошибку «container name is already in use»
- Проверить, что контейнер жив: `docker ps` (не `docker --version` — та показывает версию, даже если сам Docker Desktop не запущен)
- Посмотреть таблицы: `docker exec -it familybudget-db psql -U postgres -d familybudget -c "\dt"` (выход из постраничного просмотра — клавиша `q`)

## EF Core миграции
- Новая миграция: `dotnet ef migrations add ИмяМиграции`
- Откатить и удалить последнюю (если уже применена): `dotnet ef migrations remove --force`
- Применить к базе: `dotnet ef database update`
- Добавление составного `HasIndex(...).IsUnique()` по полю, у которого уже был одиночный auto-индекс (например, по FK) — EF Core сам сгенерирует миграцию, которая дропнет старый одиночный индекс и создаст новый составной (это нормально, не ошибка — составной индекс и так покрывает поиск по первому полю)
- По умолчанию применяется к локальной базе (из `appsettings.Development.json`). Чтобы применить к Neon — временно задать переменную окружения **в этом же окне терминала** перед командой:
  - PowerShell: `$env:ConnectionStrings__DefaultConnection="..."`
  - cmd: `set ConnectionStrings__DefaultConnection=...`
  - После — обязательно закрыть окно терминала (или сбросить переменную), чтобы она не осталась висеть и не подставилась случайно в обычный локальный запуск
- Применять к Neon нужно **из той ветки, где миграция есть**. На `main` до мержа её нет: `dotnet ef database update` ответит `No migrations were applied. The database is already up to date.` — это не значит, что миграция уже в Neon. Признак успеха — строка `Applying migration '...'`
- Проверить после применения: `dotnet ef migrations list` (в том же окне, пока переменная задана) — ни у одной миграции не должно быть пометки `(Pending)`
- Порядок, если новый код зависит от новой колонки: **сначала миграция на Neon, потом мерж в `main`** — Render деплоит сразу после мержа, и без колонки упадёт любой запрос к этой таблице (например, регистрация, которая создаёт группу). Добавление nullable-колонки старому коду на проде не мешает

## Neon — connection string
- Для миграций — **direct**-строка (без `-pooler` в хосте)
- Для рабочего приложения (Render) — **pooled**-строка (с `-pooler`)
- Neon даёт URI-формат (`postgresql://user:pass@host/db?sslmode=require`), а Npgsql ожидает: `Host=...;Port=5432;Database=...;Username=...;Password=...;SSL Mode=Require;Channel Binding=Require`
- Для pooled-строки дополнительно добавить `No Reset On Close=true` (из-за PgBouncer в режиме transaction pooling)
- Полностью снести схему, если нужно пересоздать миграции с нуля: `DROP SCHEMA public CASCADE; CREATE SCHEMA public;` в SQL Editor
- SQL Editor теперь спрятан внутри свёрнутого пункта "Postgres database" в левом меню Neon (не отдельный пункт, как раньше)
- Никогда не запускать Claude Code из терминала, где задана `ConnectionStrings__DefaultConnection` (Neon) — агент унаследует эту переменную. Для миграций — отдельный терминал, для Claude Code — отдельный

## Render
- Переменные окружения — вкладка Environment у конкретного сервиса (не на уровне Project!)
- Если поменяли только переменную окружения (не код) — выбирать **Save and deploy**, не "Save, rebuild, and deploy" (пересборка не нужна)
- При создании сервиса проверять регион (у нас — Frankfurt) и Instance Type (по умолчанию может подсовываться платный Starter вместо Free)
- Render не поддерживает .NET нативно — деплой только через Docker (Dockerfile в корне репозитория)
- Сборка (build) и запуск (runtime) — физически разные ресурсы; лимит 512 MB/0.1 CPU касается только уже запущенного сервиса, не самой сборки

## Git-процесс
- Перед началом новой работы — всегда новая ветка: `git checkout -b feature/имя` + `git push -u origin feature/имя`
- После мёржа PR: `git checkout main` && `git pull`, и уже от обновлённого main — следующая ветка
- Ветка по умолчанию `main` защищена GitHub ruleset — изменения попадают в `main` только через pull request
- Прямой push в main отклоняется с ошибкой: `GH013: Repository rule violations found for refs/heads/main` / `Changes must be made through a pull request.`
- VS Code при этом показывает только общий диалог "Can't push refs to remote. Try running "Pull" first". Pull делать **не** нужно — нажать "Show Command Output", чтобы увидеть настоящую причину
- Если коммит случайно сделан в локальном main: проверить `git log --oneline origin/main..main`; если там только ненужные коммиты и `git status` чистый — выполнить `git reset --hard origin/main`

## Проверка Identity — очистка и верификация локальной БД
- Очистить таблицы пользователей/групп целиком: `docker exec -it familybudget-db psql -U postgres -d familybudget -c "TRUNCATE TABLE \"AspNetUsers\", \"FamilyGroups\" CASCADE;"`
- Проверить, что пусто: `docker exec -it familybudget-db psql -U postgres -d familybudget -c "SELECT * FROM \"FamilyGroups\";"` и то же для `"AspNetUsers"`
- После очистки — выйти из аккаунтов в браузерах (Logout): cookie удалённых пользователей остаются
- После регистрации — проверить связку: `docker exec -it familybudget-db psql -U postgres -d familybudget -c "SELECT \"Id\", \"Email\", \"FamilyGroupId\" FROM \"AspNetUsers\";"`
- Проверка именно транзакции (не только happy path): зарегистрироваться → должна появиться 1 группа + 1 пользователь; зарегистрироваться ещё раз **тем же email** → ждём отказ "Username ... is already taken" (Identity всегда проверяет уникальность username, независимо от `RequireUniqueEmail`) → если транзакция работает, новая группа-сирота не появляется

## Identity + Blazor Server — специфика
- Login/Register/Logout — классические Razor Pages (`Areas/Identity/Pages/Account`), не Blazor-компоненты — из-за cookie и уже отправленных заголовков в интерактивных компонентах
- `AddIdentity<ApplicationUser, IdentityRole<int>>()...AddDefaultTokenProviders()`, не `AddDefaultIdentity` — последняя тащит свой встроенный UI, конфликтующий со скаффолженными страницами, и не даёт явно указать `IdentityRole<int>`
- `RequireConfirmedAccount = false` — email-сервис не настроен, иначе никто не сможет войти после регистрации
- `DbContext` регистрируется через `AddDbContextFactory`, не `AddDbContext` — иначе в Blazor Server один и тот же контекст живёт всю сессию пользователя и постепенно копит память
- Генератор `dotnet aspnet-codegenerator identity` пишет `Layout = "/Pages/Shared/_Layout.cshtml"` в `_ViewStart.cshtml` — это путь для классического Razor Pages проекта, у нас такой папки нет (мы Blazor Web App). Нужно поправить на `Layout = "_Layout";` и создать свой `Areas/Identity/Pages/Shared/_Layout.cshtml`
- `Register.cshtml.cs` требует `IEmailSender` в DI, даже при `RequireConfirmedAccount = false` — это просто зависимость в конструкторе, не связанная напрямую с этой настройкой. В `Microsoft.AspNetCore.Identity` (уже подключён через `using` ради `AddIdentity`) неожиданно есть **другой**, generic `IEmailSender<TUser>` — если регистрировать просто `IEmailSender` без явного namespace, легко попасть не в тот тип. Регистрировать нужно с полным путём: `builder.Services.AddSingleton<Microsoft.AspNetCore.Identity.UI.Services.IEmailSender, Microsoft.AspNetCore.Identity.UI.Services.NoOpEmailSender>();` — `NoOpEmailSender` уже есть готовый у Microsoft, свой писать не нужно
- `Login.cshtml.cs` (сгенерированный, нетронутый) ссылается на `LoginWith2fa`/`Lockout` страницы, которых у нас нет (генерировали только Login/Register/Logout). Сейчас это мёртвый код — 2FA нигде не включена, а `PasswordSignInAsync` в этом месте вызывается с `lockoutOnFailure: false`. Но `IdentityOptions.Lockout` при этом остаётся на дефолтах ASP.NET Core (5 неудачных попыток → блокировка), и `PasswordSignInAsync` всё равно проверяет **уже существующую** блокировку перед паролем, независимо от `lockoutOnFailure`. То есть если аккаунт когда-нибудь окажется заблокирован каким-то другим путём — при попытке входа получите 404 вместо вменяемого сообщения о блокировке. Не критично сейчас, но учесть, если будете подключать lockout/2FA

## Identity в основном Blazor-приложении (не в Identity Area)
- `Components/Routes.razor`: `<RouteView>` → `<AuthorizeRouteView>` — без этого `[Authorize]` на страницах просто игнорируется. Требует `@using Microsoft.AspNetCore.Components.Authorization` (обычно в `_Imports.razor`) — без него Razor не распознаёт `AuthorizeRouteView` как компонент (warning RZ10012) и тихо рендерит пустую страницу, без явной ошибки в консоли браузера
- Два **разных** namespace с похожими именами — та же ловушка, что и с `IEmailSender`, но теперь про Authorization: `Microsoft.AspNetCore.Components.Authorization` (компоненты `AuthorizeView`/`AuthorizeRouteView`/`AntiforgeryToken`) и `Microsoft.AspNetCore.Authorization` (сам атрибут `[Authorize]`, класс `AuthorizeAttribute`) — нужны **оба** `using`, обычно оба в `_Imports.razor`
- Форма логаута внутри Blazor-компонента (`NavMenu.razor`) — не то же самое, что `_LoginPartial.cshtml` в Identity Area (там `asp-page`, там Razor Pages). Здесь — обычный HTML `<form action="Identity/Account/Logout" method="post">` + встроенный компонент `<AntiforgeryToken />` внутри формы (не нужно вручную городить токен)
- **`ConfigureApplicationCookie` обязателен после `AddIdentity(...)`**, иначе редирект неавторизованного пользователя ведёт на дефолтный `/Account/Login` (старая MVC-конвенция), которого в проекте нет (у нас `/Identity/Account/Login`, Area) — вместо страницы логина получаем 404 через `Router.NotFoundPage`. Нужно:
  ```csharp
  builder.Services.ConfigureApplicationCookie(options =>
  {
      options.LoginPath = "/Identity/Account/Login";
      options.LogoutPath = "/Identity/Account/Logout";
      options.AccessDeniedPath = "/Identity/Account/Login";
  });
  ```

## Render — прокси и HTTPS
- Render завершает HTTPS на своём прокси, а в приложение запрос приходит по HTTP. Симптом без настройки: Mixed Content в консоли браузера и сломанные редиректы (в том числе на логин)
- Лечится `UseForwardedHeaders` — **первым** middleware в конвейере, сразу после `builder.Build()`, с очищенными `KnownIPNetworks` и `KnownProxies` (иначе заголовки от прокси Render игнорируются):
  ```csharp
  var forwardedHeadersOptions = new ForwardedHeadersOptions
  {
      ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
  };
  forwardedHeadersOptions.KnownIPNetworks.Clear();
  forwardedHeadersOptions.KnownProxies.Clear();
  app.UseForwardedHeaders(forwardedHeadersOptions);
  ```
- Что проверять на проде после изменений в Identity: cookie помечена Secure, редирект неавторизованного на логин с `ReturnUrl`, откат при повторной регистрации (группа-сирота не появляется)

## Группы и приглашения (реализовано, Этап 5)
- Код: вся логика в `Services/FamilyGroupService.cs`; страницы `Components/Pages/Group.razor` (`/group`) и `Components/Pages/Join.razor` (`/join/{token}`); пункт «My group» в `NavMenu.razor`
- Одна постоянная ссылка-приглашение на группу. Токен — `FamilyGroup.InviteToken` (`Guid?`, уникальный индекс), создаётся при первом открытии `/group`. Перегенерировать может любой участник; старая ссылка после этого перестаёт работать. Отдельного экрана создания группы нет
- Вступление — только если пользователь единственный в своей группе; его личная группа удаляется вместе с данными. Это явное действие: страница показывает предупреждение и кнопку «Join», автоматического вступления после логина/регистрации нет
- Порядок важен: сначала перенести пользователя в новую группу, потом удалять старую — FK пользователя на группу каскадный, иначе удалится сам пользователь
- Выход — только если в группе есть другие участники. Создаётся новая личная «My Family», а пользователю показывается ссылка старой группы для возврата (работает, пока её не перегенерировали)
- `JoinAsync` и `LeaveAsync` в начале транзакции блокируют строку своей группы (`SELECT ... FOR UPDATE`, сырой SQL под PostgreSQL). От какой гонки это защищает — в комментарии к `LockGroupAsync`
- Ожидаемые исходы (`InvalidToken`, `AlreadyMember`, `HasOtherMembers`, `SoleMember`) возвращаются enum-значениями; неожиданные сбои — исключения, в сервисе не ловятся
- Живого обновления нет: изменения, сделанные другим участником, видны после перезагрузки страницы
- Ссылка-приглашение проходит через логин/регистрацию как `returnUrl`. В `OnPostAsync` страниц Login и Register `ReturnUrl` присваивается явно: сгенерированный код задавал его только в `OnGetAsync`, и после неудачной попытки (страница показывается заново) адрес возврата терялся
- Клиентская валидация на страницах Identity не работает: `_ValidationScriptsPartial.cshtml` ссылается на `~/lib/jquery-validation/...`, а в `wwwroot/lib` есть только `bootstrap`. Любая ошибка ввода уходит на сервер и приводит к повторному показу формы
- Ручная проверка: нужны 2–3 аккаунта одновременно — разные браузеры или окна инкогнито. Пароль, проходящий политику Identity: с цифрой, заглавной буквой и спецсимволом
- Каскадное удаление группы проверено тестами (Этап 6): вступление работает и при вложенных категориях, и при продуктах, магазинах и покупках — `Restrict`-связи (родитель категории, Purchase→Product, Purchase→User) каскаду не мешают
- Подробно: ТЗ 2.2, DB_SCHEMA, CLAUDE.md (Architecture Decision #14)

## Категории (реализовано, Этап 6)
- Код: логика — `Services/CategoryService.cs` (+ `CategoryResults.cs`), дерево по умолчанию — `Services/DefaultCategories.cs`, страница `Components/Pages/Categories.razor` (`/categories`), пункт «Categories» в `NavMenu.razor`
- Набор по умолчанию (26 категорий, 8 верхнего уровня, названия по-русски) определён в одном месте — `DefaultCategories.cs`. `DefaultCategories.AddTo` вызывается при регистрации и при выходе из группы, до `SaveChanges`, внутри существующих транзакций
- Существующим группам набор досеян миграцией с данными `SeedDefaultCategories` (вставка только группам без категорий; `Down()` пустой намеренно — после миграции категории уже пользовательские данные). Список в миграции — замороженная копия, при изменении `DefaultCategories` её не трогать. Применена на Neon 17.11.2026
- Удаление категории удаляет всё поддерево одним `DELETE`; продукты из него остаются с `CategoryId = NULL`, покупки не затрагиваются. Имена могут повторяться. Соседние категории показываются по порядку `Id`
- Перенос категории к другому родителю (с проверкой на цикл) не реализован
- Подробно: CLAUDE.md (Architecture Decisions #15, #16), DB_SCHEMA

## Продукты и магазины (реализовано, Этап 6)
- Код: `Services/NameNormalizer.cs`, `Services/ProductService.cs` (+ `ProductResults.cs`), `Services/StoreService.cs` (+ `StoreResults.cs`). Страниц пока нет — их будет использовать форма покупки
- Названия продуктов и магазинов нормализуются в одном месте (`NameNormalizer`) при каждой записи и поиске: обрезка по краям, любые пробельные последовательности (включая табуляцию и неразрывный пробел) → один пробел, нижний регистр. Категории не нормализуются
- Благодаря нижнему регистру существующий уникальный индекс `(FamilyGroupId, Name)` даёт уникальность продуктов без учёта регистра — миграция не понадобилась
- Продукт: «найти или создать» (категория задаётся только при создании), переименование (в занятое имя — `NameTaken`, без слияния), смена/сброс категории, удаление (если есть покупки — `InUse`). Гонки: `23505` на `IX_Products_FamilyGroupId_Name` → перечитать; `23503` при удалении → `InUse`
- Магазин: создание (повторы разрешены), переименование, удаление (у покупок магазин становится `NULL`)
- Слияние продуктов — запланировано, не сделано
- Подробно: CLAUDE.md (Architecture Decision #17)

## Покупки (сервис реализован, Этап 6)
- Код: `Services/PurchaseService.cs` (+ `PurchaseResults.cs`). Страниц пока нет
- `Price` — цена за единицу, любого знака (0 и отрицательные — строки скидок); трата = `Price × Quantity`. `LineTotal` в списке — с 5 знаками после запятой (2 + 3), округление для показа — забота страниц/отчётов
- Точность колонок: `Price` `numeric(10,2)`, `Quantity` `numeric(10,3)` (миграция `PurchasePrecision`, применена на Neon 22.11.2026). Лишние знаки после запятой — ошибка, без округления
- Добавление нескольких позиций — всё или ничего, одной транзакцией; все ошибки возвращаются с номерами позиций (несколько ошибок одной позиции — несколько записей с одним номером)
- PostgreSQL прерывает транзакцию после любой ошибки, перечитать в ней уже нельзя. Поэтому при гонке на создании продукта (`23505` на `IX_Products_FamilyGroupId_Name`) вся попытка повторяется один раз в новом контексте и новой транзакции
- Дата позже завтрашней (UTC) заменяется на завтрашнюю, результат сообщает `DateCorrected`. «Сегодня» берётся из `TimeProvider` (зарегистрирован `TimeProvider.System`); в тестах — свой подкласс `TimeProvider` без NuGet-пакета
- Подробно: CLAUDE.md (Architecture Decision #18), ТЗ 2.3, DB_SCHEMA

## Blazor / Razor — разметка
- Дробные числа в inline-CSS через `@(...)` — осторожно. Наблюдали на Этапе 6: отступ `padding-left: @(row.Depth * 1.5)rem` не работал на первом уровне, а на втором (целое `3rem`) работал; замена на `@(row.Depth * 24)px` починила. Вероятная причина (в DevTools не подтверждали): Razor форматирует числа по культуре сервера, и при русских/болгарских региональных настройках получается `1,5rem`, которое браузер молча игнорирует. Использовать целые значения или `CultureInfo.InvariantCulture`

## Тесты
- Запуск из корня репозитория: `dotnet test tests/FamilyBudget.Tests`. Нужен запущенный Docker Desktop: Testcontainers сам поднимает отдельный контейнер `postgres:18` и удаляет его после прогона (локальную базу `familybudget-db` тесты не трогают)
- Время: сами тесты ~1 с, вся команда ~9 с; первый запуск на машине дольше — скачиваются образы `postgres:18` и `testcontainers/ryuk`
- Схема в тестовой базе создаётся настоящими миграциями (`Database.MigrateAsync()`), так что сломанная миграция уронит тесты
- Проект: `tests/FamilyBudget.Tests/` — `PostgresFixture.cs` (контейнер + фабрика контекста, свойство `ConnectionString`), `TestData.cs` (создание групп, пользователей, категорий и продуктов напрямую через `DbContext`; `ExpectedDefaultTree` — ожидаемое дерево категорий полными путями, отдельно от продакшен-кода). Тесты: `FamilyGroupServiceTests.cs` (15), `DefaultCategoriesTests.cs` (4), `CategoryServiceTests.cs` (14 с учётом theory), `SeedDefaultCategoriesMigrationTests.cs` (1) — всего 34. Каждый тест создаёт свои данные и не зависит от других
- С Этапа 6 (продукты и магазины) добавлены `NameNormalizerTests.cs` (без базы), `ProductServiceTests.cs`, `StoreServiceTests.cs`, `JoinCascadeTests.cs`; `TestData.cs` умеет создавать магазины и вставлять покупки напрямую. Всего 80 тестов (с учётом theory)
- С сервисом покупок добавлены `PurchaseServiceTests.cs` и `PurchasePrecisionMigrationTests.cs` (проверяет типы колонок через `information_schema.columns`), в `JoinCascadeTests.cs` — вступление в группу с покупками. Всего 122 теста (на 22.11.2026)
- Невидимые символы в тестах (например, неразрывный пробел) писать escape-последовательностью `\u00A0`, а не литералом — литерал не виден ни в diff, ни в редакторе
- Тест миграции с данными (`SeedDefaultCategoriesMigrationTests`): создаёт в том же контейнере отдельную пустую базу, мигрирует её через `IMigrator` до миграции, предшествующей проверяемой (находит её по `GetMigrations()`, без хардкода таймстемпа), вставляет данные «как до миграции» и мигрирует до последней
- Пакет — `xunit.v3.mtp-off`, а не `xunit.v3`. Обычный `xunit.v3` включает Microsoft Testing Platform, и на .NET 10 SDK `dotnet test` падает с `Testing with VSTest target is no longer supported by Microsoft.Testing.Platform on .NET 10 SDK and later`. Альтернатива — `global.json` с `"test": { "runner": "Microsoft.Testing.Platform" }` в корне, но его читают все команды `dotnet` в корне, поэтому не стали
- `FamilyBudget.csproj` лежит в корне, и файла решения нет намеренно: рядом с `.csproj` он сделал бы неоднозначными `dotnet build`, `dotnet ef` и сборку в Dockerfile, которые запускаются без имени проекта
- В `FamilyBudget.csproj` стоит `<DefaultItemExcludes>...;tests/**</DefaultItemExcludes>` — без этого основной проект компилировал бы файлы тестового (включая сгенерированные в `tests/.../obj`)
- В CI тесты — шаг `Test` внутри проверки `build`; упавший тест блокирует мерж. Лог: страница PR → проверка `CI / build` → раскрыть шаг `Test` (или вкладка Actions → запуск `CI` → `build` → `Test`); итог в последней строке, `Passed: N`
- Не покрыто тестами: гонки (блокировка строки группы), страницы Identity, Blazor-компоненты

## Claude Code — файлы с правами (permissions)
- `.claude/settings.json` — общие правила прав (allow / deny), закоммичен в репозиторий; меняется только через PR. Правила deny побеждают allow на любом уровне настроек. Агенту запрещено редактировать `.claude/**`, поэтому эти файлы правятся вручную
- В личном `.claude/settings.local.json` разрешена ровно одна команда PowerShell — гудок агента: `Bash(powershell.exe -NoProfile -Command "[console]::beep(880,300)")`. Не разрешать `powershell.exe ... *` со звёздочкой: через PowerShell агент обошёл бы все запреты из `settings.json`
- Личные хуки в `C:\Users\Asus\.claude\settings.json` (вне репозитория): `Stop` — один гудок, когда Claude Code закончил ответ и ждёт; `Notification` с `matcher` `permission_prompt|elicitation_dialog` — два гудка, когда просит разрешение. Применяются в новой сессии; проверить — `/hooks`
- `.claude/settings.local.json` — личные правила (накапливаются при ответе "don't ask again" в Claude Code). Его **нет** в `.gitignore` репозитория — он игнорируется только глобальным git ignore-файлом `C:\Users\Asus\.config\git\ignore` на Windows-машине. Проверить: `git check-ignore -v .claude/settings.local.json`
- Посмотреть действующие правила в Claude Code: `/permissions`

## Claude Code — процесс разработки через `/feature`
- Запуск в новой сессии Claude Code: `/feature <путь к файлу ТЗ>`. Скилл — `.claude/skills/feature/SKILL.md`: ветка → план → ждёт «ок» → реализация → `dotnet build` → проверка подагентом `verifier` → коммит → push. PR и мерж — вручную
- ТЗ лежат вне репозитория, в `D:\Cowork\PetProject\specs\` (скилл останавливается, если в рабочей папке есть незакоммиченные изменения). Обязательные разделы ТЗ: Branch, Goal, Requirements, Acceptance criteria, Out of scope
- Если ветка из ТЗ уже существует, скилл продолжает работу в ней, а не создаёт новую
- Отчёт агента — `D:\Cowork\PetProject\report.md`; перезаписывается каждой задачей
- Проверяющий подагент — `.claude/agents/verifier.md`: сверяет изменения с дословным текстом ТЗ и утверждённым планом, сам ничего не меняет; при FAIL коммита нет
- Новый подагент виден только в новой сессии: при добавлении файла в `.claude/agents/` в уже открытой сессии получаем `Agent type '...' not found`
- Перед «ок» агенту остановить локально запущенное приложение — иначе `dotnet build` падает на заблокированном `FamilyBudget.exe`
- Агенту запрещено (правила в `.claude/settings.json`): править `.claude/**` и `Migrations/**`, push в `main`, force push, удаление веток. Миграции он создаёт только после явного разрешения; к Neon не прикасается никогда
- Скилл требует гудок (`powershell.exe -NoProfile -Command "[console]::beep(880,300)"`) последним действием перед каждой остановкой в ожидании ввода и в конце. Команда разрешена только в личном `settings.local.json` — на другой машине агент сначала попросит разрешение
- Коммит и push — отдельными командами (или через `&&`), push только после успешного коммита. Было: при `commit ; push` коммит не прошёл, а push выполнился, и ветка на GitHub временно указывала на коммит `main`
- В браузере фичу агент не проверяет: сценарий ручной проверки он пишет в отчёт, проходить его — нам
- Миграция с данными (SQL в теле `Up()`) при запрете агенту на `Migrations/**`: агент с разрешения создаёт пустую миграцию (`dotnet ef migrations add`), проверяет, что `Up()`/`Down()` пустые и снапшот не изменился, и останавливается. Тело вписывается вне сессии агента (Claude из чата через доступ к папке), после «continue» агент пишет тест на миграцию и доводит задачу. Так сделано с `SeedDefaultCategories`

# Полезные команды и заметки

## Запуск
- Запуск локально с HTTPS: `dotnet run --launch-profile https`
- Разово, если браузер ругается на сертификат: `dotnet dev-certs https --trust`

## Docker / локальный Postgres
- Поднять локальную БД: `docker run --name familybudget-db -e POSTGRES_PASSWORD=devpassword -e POSTGRES_DB=familybudget -p 5432:5432 -d postgres:18`
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
- **Не проверено**: не упрётся ли каскадное удаление группы в `Restrict`-связи (Purchase→Product, Purchase→User, родитель категории). Проверить на Этапе 6, когда появятся данные
- Подробно: ТЗ 2.2, DB_SCHEMA, CLAUDE.md (Architecture Decision #14)

## Claude Code — файлы с правами (permissions)
- `.claude/settings.json` — общие правила прав (allow / deny), закоммичен в репозиторий; меняется только через PR. Правила deny побеждают allow на любом уровне настроек. Агенту запрещено редактировать `.claude/**`, поэтому эти файлы правятся вручную
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
- В браузере фичу агент не проверяет: сценарий ручной проверки он пишет в отчёт, проходить его — нам

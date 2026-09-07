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
- По умолчанию применяется к локальной базе (из `appsettings.Development.json`). Чтобы применить к Neon — временно задать переменную окружения **в этом же окне терминала** перед командой:
  - PowerShell: `$env:ConnectionStrings__DefaultConnection="..."`
  - cmd: `set ConnectionStrings__DefaultConnection=...`
  - После — обязательно закрыть окно терминала (или сбросить переменную), чтобы она не осталась висеть и не подставилась случайно в обычный локальный запуск

## Neon — connection string
- Для миграций — **direct**-строка (без `-pooler` в хосте)
- Для рабочего приложения (Render) — **pooled**-строка (с `-pooler`)
- Neon даёт URI-формат (`postgresql://user:pass@host/db?sslmode=require`), а Npgsql ожидает: `Host=...;Port=5432;Database=...;Username=...;Password=...;SSL Mode=Require;Channel Binding=Require`
- Для pooled-строки дополнительно добавить `No Reset On Close=true` (из-за PgBouncer в режиме transaction pooling)
- Полностью снести схему, если нужно пересоздать миграции с нуля: `DROP SCHEMA public CASCADE; CREATE SCHEMA public;` в SQL Editor
- SQL Editor теперь спрятан внутри свёрнутого пункта "Postgres database" в левом меню Neon (не отдельный пункт, как раньше)

## Render
- Переменные окружения — вкладка Environment у конкретного сервиса (не на уровне Project!)
- Если поменяли только переменную окружения (не код) — выбирать **Save and deploy**, не "Save, rebuild, and deploy" (пересборка не нужна)
- При создании сервиса проверять регион (у нас — Frankfurt) и Instance Type (по умолчанию может подсовываться платный Starter вместо Free)
- Render не поддерживает .NET нативно — деплой только через Docker (Dockerfile в корне репозитория)
- Сборка (build) и запуск (runtime) — физически разные ресурсы; лимит 512 MB/0.1 CPU касается только уже запущенного сервиса, не самой сборки

## Git-процесс
- Перед началом новой работы — всегда новая ветка: `git checkout -b feature/имя` + `git push -u origin feature/имя`
- После мёржа PR: `git checkout main` && `git pull`, и уже от обновлённого main — следующая ветка

## Проверка Identity — очистка и верификация локальной БД
- Очистить таблицы пользователей/групп целиком: `docker exec -it familybudget-db psql -U postgres -d familybudget -c "TRUNCATE TABLE \"AspNetUsers\", \"FamilyGroups\" CASCADE;"`
- Проверить, что пусто: `docker exec -it familybudget-db psql -U postgres -d familybudget -c "SELECT * FROM \"FamilyGroups\";"` и то же для `"AspNetUsers"`
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

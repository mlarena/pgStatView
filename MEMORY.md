# MEMORY.md — рабочая память по проекту pgStatView

> Этот файл — единственный источник «памяти» между сессиями. При старте новой сессии
> сначала читается он, затем `task.md` и `план_этапN.md` по мере необходимости.
> После каждой значимой работы раздел «Журнал» и «Фактическое состояние» обновляются.

Последнее обновление: 2026-10-26

---

## 1. Что за проект

**pgStatView** — автономный веб-клиент для централизованного мониторинга PostgreSQL через
`pg_stat_statements`: параллельный опрос нескольких целевых БД, накопление истории метрик в
собственной мета-БД, графики сравнения (D3.js v7), фильтрация по таблицам, визуализация
планов (PEV2), real-time через SignalR.

Документы-источники (в корне, читать при необходимости):

| Файл | Содержание |
|------|-----------|
| `task.md` | Исходное ТЗ: архитектура с мета-БД, D3, API, риски |
| `общее_описание.txt` | Общее описание системы, поток данных, стек, ограничения |
| `план_этап1.md` | Мета-БД, CRUD подключений, JWT-auth, базовый UI, скелет сборщика |
| `план_этап2.md` | Реальный сбор из `pg_stat_statements`, Snapshot/MetricPoint, retention, API метрик |
| `план_этап3.md` | D3.js графики, cookie-auth для страниц, tooltip/zoom/live |
| `план_этап4.md` | Топ-запросы, фильтр по таблицам, детальная страница запроса, EXPLAIN JSON |
| `план_этап5.md` | PEV2, сохранение планов в мета-БД, сравнение планов |
| `план_этап6.md` | SignalR, Docker, health checks, полировка |

---

## 2. Архитектура (зафиксированные решения)

```
PgStatView.Web (ASP.NET Core MVC + REST API + SignalR + BackgroundServices)
├── PgStatView.Core       — модели, DTO, интерфейсы, QueryNormalizer (без инфраструктуры)
├── PgStatView.Meta       — EF Core, MetaDbContext, миграции, сервисы мета-БД
├── PgStatView.Collector  — Npgsql (raw SQL к целевым БД), фоновые службы сбора/retention
└── PgStatView.Tests      — xUnit, ссылается на все проекты (включая Web)
```

- **Мета-БД** — SQLite (`Data Source=pgstatview.db`), EF Core + миграции. Таблицы:
  `MonitoredDatabase`, `Snapshot`, `MetricPoint`, `User`, `CollectionInterval`, `ExplainPlan`.
- **Целевые БД** — PostgreSQL 12–17, только чтение системных каталогов, только Npgsql.
- Сравнение между серверами — по `QueryHash` (SHA256 от нормализованного текста), **не** по `queryid`.
- Фронтенд — без SPA и без сборщика: MVC Views + ES-модули в `wwwroot/js`, D3 v7 и PEV2
  **локально** (CDN запрещён).
- Аутентификация: JWT для API (этап 1), cookie-auth параллельно для MVC-страниц (этап 3).

### Расхождения в документах (важно, решения ниже)

| Вопрос | `task.md` / `общее_описание` | `план_этап1.md` + текущий код | Принято |
|--------|------------------------------|-------------------------------|---------|
| Пароль целевой БД | шифруется `IDataProtector` | хранится **открыто** «по требованию заказчика» | открыто (как в модели и плане 1); шифрование — опционально позже |
| Мета-БД | PostgreSQL/SQLite | SQLite | SQLite |
| Версия .NET | .NET 8 | .NET 10 | **net10.0** |
| Файл решения | `PgStatView.sln` | — | `PgStatView.slnx` (формат slnx) |

---

## 3. Ограничения окружения и правила работы

- Разработка — **Windows** (PowerShell), запуск в продакшене — **Linux Debian**.
  Пути/команды — кроссплатформенные; в коде использовать `Path.Combine`, не хардкодить `\`.
- **Кириллица в консоли запрещена** — вывод команд и логи в консоль только латиницей
  (комментарии в коде на русском допустимы, они уже есть в проекте).
- Никаких CDN и внешних зависимостей на фронте — всё в `wwwroot`.
- Секреты (JWT-ключ, пароли) — через конфиг/env, не логировать connection strings.
- Не выполнять git-мутации (`commit`, `push`, `reset`, `rebase`) без явного разрешения.
- `EXPLAIN` только `(FORMAT JSON)` **без** `ANALYZE`, `statement_timeout = 5s`.

---

## 4. Фактическое состояние репозитория (2026-10-26)

Ветка `main`, последний коммит `873f71d "1 этап"`. Рабочее дерево чистое.

**Сборка решения падает** — `PgStatView.Core/Models/MonitoredDatabase.cs` ссылается на
несуществующие типы `Snapshot`, `MetricPoint`, `ExplainPlan` (3 ошибки CS0246).

### Что реально есть

| Проект | Содержимое |
|--------|-----------|
| `PgStatView.Core` | только `Models/MonitoredDatabase.cs` (поля этапов 1–2, plaintext-пароль, `GetConnectionString()`) |
| `PgStatView.Meta` | пусто (только csproj со ссылкой на Core) |
| `PgStatView.Collector` | пусто (только csproj со ссылкой на Core) |
| `PgStatView.Tests` | `UnitTest1.cs` (шаблонный), xUnit 2.9.3 + coverlet, ссылки на все проекты |
| `PgStatView.Web` | шаблон `dotnet new mvc`: `Program.cs` без DI-регистраций, `HomeController`, Views Home/Shared, `wwwroot` с bootstrap/jquery, `appsettings.json` без строк подключения и JWT |

Прочее: `PgStatView.slnx` (5 проектов), `.gitignore` (bin/obj, `.vscode/`, `appsettings.Development.json`,
явные шаблоны артефактов NuGet/MSBuild; `*.db` **не** исключён), `PgStatView.Web/appsettings.Development.json`
уже отслеживается git.

**Git-гигиена:** артефакты сборки (`bin/`, `obj/`, `project.assets.json`, `*.nuget.dgspec.json`,
`project.nuget.cache`) были закоммичены в `873f71d`; 2026-10-26 сняты с отслеживания
(`git rm -r --cached`), в `.gitignore` добавлен явный блок «.NET build artifacts». Изменение
не закоммичено.

### Чего ещё нет (нужно добавить по планам)

- Пакеты: `Npgsql`, `Microsoft.EntityFrameworkCore.Sqlite` (+ `.Design`/`.Tools`), `BCrypt.Net-Next`,
  `Microsoft.AspNetCore.Authentication.JwtBearer`, SignalR-клиент (этап 6).
- Модели Core: `Snapshot`, `MetricPoint`, `User`, `CollectionInterval`, `ExplainPlan`;
  DTO, интерфейсы (`IStatsCollector`, `IPasswordService`, `ICollectionIntervalService`), `QueryNormalizer`.
- Meta: `MetaDbContext`, `DbInitializer`, миграции, сервисы CRUD/User/Interval/SnapshotWriter/Retention.
- Collector: `StatsCollector`, `CollectionBackgroundService`, `TargetDbInspector`, `TableCache`, `RetentionBackgroundService`.
- Web: контроллеры API, JWT/cookie auth, страницы, `wwwroot/js` (d3, chart-base, multi-line, tooltip, pages), Docker, health checks.

---

## 5. Статус по этапам

Легенда: `[x]` сделано, `[~]` частично, `[ ]` не начато.

### Этап 1 — Мета-БД + управление подключениями — **в работе (~5 %)**
- [~] 1. Скелет решения (есть, но не собирается)
- [~] 2. Модели Core + DTO (1 из 5 моделей, DTO нет)
- [ ] 3. MetaDbContext + миграции + DbInitializer
- [ ] 4. CRUD-сервисы (MonitoredDatabase, User, CollectionInterval)
- [ ] 5. API-контроллеры (Databases, Auth, CollectionInterval)
- [ ] 6. JWT-аутентификация
- [ ] 7. Базовый UI (список/создание/редактирование БД, логин)
- [ ] 8. Фоновый сборщик-скелет
- [ ] 9. Тесты этапа 1

### Этап 2 — Сбор статистики и снимки — не начато
### Этап 3 — Web UI с D3.js — не начато
### Этап 4 — Таблицы и детализация — не начато
### Этап 5 — PEV2 — не начато
### Этап 6 — Real-time, Docker, health checks — не начато

---

## 6. Ближайшие шаги (порядок работ)

1. Создать недостающие модели Core (`Snapshot`, `MetricPoint`, `User`, `CollectionInterval`,
   `ExplainPlan`) — чтобы решение собралось. Поля брать из `план_этап1.md` §3, `план_этап2.md` §5,
   `план_этап5.md` §4.1. В `MetricPoint` сразу предусмотреть `QueryHash`, `MetricKind`, `Tables`.
2. DTO + DataAnnotations (`MonitoredDatabaseDto`, `LoginDto`, `RegisterDto`, `CollectionIntervalDto`).
3. `PgStatView.Meta`: `MetaDbContext` (SQLite, индексы из плана 1), `DbInitializer`, первая миграция.
4. Сервисы Meta: CRUD БД, пользователи (BCrypt), интервал сбора.
5. Web: DI, JWT, контроллеры API, `/api/databases/{id}/test` через Npgsql.
6. MVC Views: список/форма БД, логин, layout с навигацией.
7. Collector: скелет `CollectionBackgroundService` с живым перечитыванием интервала.
8. Тесты этапа 1 + зелёная сборка без предупреждений.

---

## 7. Конвенции кода

- Пространства имён = имя проекта (`PgStatView.Core.Models`, `PgStatView.Meta.Data`, …).
- File-scoped namespaces, `ImplicitUsings`, `Nullable` включены.
- Модели — POCO без атрибутов EF (конфигурация через Fluent API в `OnModelCreating`).
- Все времена — UTC (`DateTime.UtcNow`).
- XML-документация на русском у публичных типов/свойств (как в `MonitoredDatabase.cs`).
- Строковые id целевых БД — slug (`prod-main`), не GUID.
- Логи — через `ILogger<T>`, структурированно, без секретов.
- Тесты: xUnit, `[Fact]`/`[Theory]`, имена `Method_Scenario_Expected`.

---

## 8. Открытые вопросы к заказчику

1. Шифровать ли пароли целевых БД (`IDataProtector`) — сейчас в модели открытый текст.
2. Мета-БД: остаётся SQLite или нужен PostgreSQL (в `task.md` упоминались оба)?
3. Нужен ли soft-delete для `MonitoredDatabase` (в `task.md` есть, в плане 1 — физическое удаление).

---

## 9. Журнал

### 2026-10-26 — инициализация памяти
- Прочитаны `task.md`, `общее_описание.txt`, `план_этап1..6.md`, состояние всех проектов.
- Зафиксировано: решение не собирается (отсутствуют `Snapshot`, `MetricPoint`, `ExplainPlan`).
- Создан этот файл. Код не изменялся.

### 2026-10-26 — чистка git от артефактов сборки
- `.gitignore`: добавлен явный блок «.NET build artifacts (NuGet restore / MSBuild)» —
  `project.assets.json`, `*.nuget.dgspec.json`, `project.nuget.cache`, `*.assets.cache`,
  `*.nuget.g.props/targets`, `*.AssemblyInfo.cs`, `*.FileListAbsolute.txt`, `*.sourcelink.json` и др.
- Из индекса git снято 311 файлов `bin/` и `obj/` (`git rm -r --cached`, файлы на диске сохранены).
- Проверено: `git ls-files` больше не содержит артефактов, `git check-ignore` подтверждает правила.
- Ничего не закоммичено — изменения в индексе ждут коммита пользователя.

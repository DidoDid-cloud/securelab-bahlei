# Карта архітектури

Це початкова карта. Під час ЛР 1 доповніть її власним трасуванням запиту,
конкретними файлами та спостереженнями з DevTools і журналу PostgreSQL.

## Компоненти

| Компонент | Розташування | Відповідальність |
|---|---|---|
| Browser client | `src/SecureLab.Api/Client/` | Надсилає HTTP-запити, безпечно показує відповідь через DOM API |
| Presentation | `Presentation/` | Описує endpoints, читає зовнішні параметри, формує HTTP-відповідь |
| Application | `Application/` | Виконує сценарій отримання списку або деталей інциденту |
| Data | `Data/` | Відображає C#-сутності на PostgreSQL через EF Core/Npgsql |
| PostgreSQL | `infra/compose.yaml` | Зберігає навчальні дані у локальному контейнері |

## Підготовлений наскрізний маршрут

```text
submit/click у Client/app.js
  → GET /api/incidents або GET /api/incidents/{id}
  → Presentation/Endpoints/IncidentEndpoints.cs
  → Application/Incidents/IncidentQueries.cs
  → Data/SecureLabDbContext.cs
  → PostgreSQL
  → response DTO у Presentation/Contracts/
  → JSON
  → textContent/createTextNode у Client/app.js
```

## Реалізований маршрут: підсумок за severity (ЛР 1)

```text
клік по кнопці "Показати підсумок" у Client/index.html
  -> loadSeveritySummary() у Client/app.js
  -> GET /api/incidents/severity-summary[?status=]
  -> Presentation/Endpoints/IncidentEndpoints.cs: GetSeveritySummaryAsync
    (валідація status за allowlist IncidentStatus, 400 при некоректному значенні)
  -> Application/Incidents/IncidentQueries.cs: GetSeveritySummaryAsync
    (AsNoTracking -> GroupBy(Severity) -> Count() -> ToListAsync)
  -> Data/SecureLabDbContext.cs: DbSet<Incident> Incidents, таблиця "incidents"
  -> PostgreSQL: GROUP BY severity
  -> доповнення відсутніх severity (політика "повний перелік рівнів", count: 0)
  -> сортування за критичністю (Critical -> High -> Medium -> Low)
  -> Presentation/Contracts/IncidentResponses.cs: IncidentSeveritySummaryResponse(Severity, Count)
  -> JSON-масив
  -> renderSeveritySummary() у Client/app.js -> textContent
```

**Політика нульових груп:** обрано *повний перелік рівнів* — після агрегації код доповнює severity, відсутні в таблиці, значенням `count: 0` (реалізація: `Enum.GetValues<IncidentSeverity>()` + `GetValueOrDefault` у `IncidentQueries.GetSeveritySummaryAsync`). На baseline seed дає 4 елементи: Critical (0), High (1), Medium (1), Low (1).

**Порядок:** явний порядок критичності (не SQL/лексикографічний, оскільки `Severity` зберігається як `text` через `HasConversion<string>()`), реалізовано сортуванням за числовим значенням enum після матеріалізації агрегату в пам'яті.

## Межі довіри

Доповніть таблицю щонайменше трьома конкретними спостереженнями.

| Межа | Чому даним ще не можна довіряти | Де перевіряємо або обмежуємо |
|---|---|---|
| Browser -> API | method, URL, path parameter `id`, query parameter `status` повністю контролює клієнт | маршрутне обмеження `:guid` на `/{id:guid}`; allowlist-валідація `status` через `Enum.TryParse`+`Enum.IsDefined`, 400 Validation Problem Details при некоректному значенні |
| API -> PostgreSQL | збережений у БД текст не стає безпечним автоматично лише тому, що вже пройшов через систему раніше | EF Core параметризує LINQ-запити (захист від SQL-ін'єкції); `AsNoTracking()` явно позначає read-only сценарій |
| API -> Browser | право прочитати сутність (entity) не означає право одержати всі її поля | явна проєкція в `IncidentQueries` (`Select` формує лише дозволені поля DTO); `IncidentDetailsResponse` не містить `OwnerUserId`, `email`, внутрішні коментарі (`IsInternal == true`) |
| Дані response -> DOM | текстове значення з JSON, включно зі збереженим раніше користувацьким вводом, не можна інтерпретувати як HTML | `textContent`/`document.createTextNode` в `app.js` (у т.ч. `renderSeveritySummary`), а не `innerHTML`; підтверджено тестом `ClientScript_DoesNotUseDangerousInnerHtmlSink` |


## Конфігураційні входи

- `global.json` — версія .NET SDK;
- `src/SecureLab.Api/appsettings*.json` — режим міграцій і локальний connection string;
- `infra/compose.yaml` — версія PostgreSQL, порт і локальні навчальні облікові дані;
- змінна середовища `ConnectionStrings__SecureLab` — безпечний спосіб перевизначити connection string поза репозиторієм.

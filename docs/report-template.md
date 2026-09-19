# Звіт до лабораторної роботи № 1

## 1. Ідентифікація стану

- Варіант: 2-A «Трекер інцидентів».
- Гілка: `lab/1-system` (злита в `main`).
- Фінальний тег: `v0.1.0`.
- Commit hash: `7fbedca04f652553651f05e22fee2e9b18514c53`.

## 2. Змінений маршрут

Реалізовано наскрізне розширення `GET /api/incidents/severity-summary`.

```text
клік по кнопці "Показати підсумок" (Client/index.html)
  -> loadSeveritySummary() у Client/app.js
  -> GET /api/incidents/severity-summary[?status=]
  -> Presentation/Endpoints/IncidentEndpoints.cs: GetSeveritySummaryAsync
    (allowlist-валідація query parameter status, 400 при некоректному значенні)
  -> Application/Incidents/IncidentQueries.cs: GetSeveritySummaryAsync
    (AsNoTracking -> GroupBy(Severity) -> Count() -> ToListAsync)
  -> Data/SecureLabDbContext.cs: DbSet<Incident> Incidents, таблиця "incidents"
  -> PostgreSQL: GROUP BY severity
  -> доповнення відсутніх severity до повного переліку (count: 0)
  -> сортування за критичністю (Critical -> High -> Medium -> Low)
  -> Presentation/Contracts/IncidentResponses.cs: IncidentSeveritySummaryResponse(Severity, Count)
  -> JSON-масив -> renderSeveritySummary() у Client/app.js -> textContent
```

До зміни endpoint повертав `501 Not Implemented` (baseline-заглушка).

## 3. Виконані зміни

1. Додано `IncidentSeveritySummaryResponse(string Severity, int Count)` у `Presentation/Contracts/IncidentResponses.cs` — окремий DTO без полів entity.
2. Додано `IncidentQueries.GetSeveritySummaryAsync(IncidentStatus? status, CancellationToken)` у `Application/Incidents/IncidentQueries.cs`: read-only агрегація через `AsNoTracking -> GroupBy -> Count() -> ToListAsync`; політика нульових груп — **повний перелік рівнів** (доповнення відсутніх `IncidentSeverity` значенням `count: 0` через `Enum.GetValues<IncidentSeverity>()`); сталий порядок — **явна критичність** (Critical -> High -> Medium -> Low, сортування за числовим значенням enum після матеріалізації); структуроване логування (`logger.LogInformation` із шаблоном і параметрами `GroupCount`/`Status`, без чутливих даних).
3. Замінено baseline-заглушку `501` на реальний endpoint у `Presentation/Endpoints/IncidentEndpoints.cs`: `GetSeveritySummaryAsync` з DI (`IncidentQueries`), опційний query parameter `status` з allowlist-валідацією (той самий патерн, що вже є в `GetListAsync`), типізована OpenAPI-метадата `.Produces<IReadOnlyList<IncidentSeveritySummaryResponse>>()`.
4. Додано в `Client/index.html` семантичну секцію з кнопкою `#summary-button`, статусом `#summary-status` (`role="status"`) і контейнером результату `#summary-list`.
5. Додано в `Client/app.js` `loadSeveritySummary()`/`renderSeveritySummary()` із трьома станами: завантаження (перед запитом, кнопка блокується), порожній результат (захисна гілка — не спрацьовує на поточному seed через обрану політику "повний перелік"), безпечна помилка (фіксоване повідомлення без деталей). Вивід — виключно через `textContent`.
6. Оновлено `tests/http/incidents.http`: коментар `501->200` для базового запиту без дублювання, додано сценарії з `status=Triaged` (200) і `status=NotAStatus` (400).
7. Доповнено `docs/architecture.md` реалізованим маршрутом, обраною політикою/порядком і таблицею меж довіри.

## 4. Перевірка

| ID | Передумови | Дія | Очікувано | Фактично | Доказ |
|---|---|---|---|---|---|
| T-01 | PostgreSQL healthy, API запущено | `GET /health` | 200, `{"status":"ready"}` | 200 OK, `{"status":"ready"}` | curl -i |
| T-02 | відновлений seed | `GET /api/incidents?status=Triaged` | 200, 1 інцидент (Medium/Triaged) | 200 OK, 1 елемент "Підозрілий лист із вкладенням" | curl -i |
| T-03 | відновлений seed | `GET /api/incidents?status=Resolved` | 200 з `[]` | 200 OK, `[]` | curl -i |
| T-04 | відновлений seed | `GET /api/incidents/99999999-9999-9999-9999-999999999999` | 404 Problem Details | 404, traceId `00-959ea21ed4e2929422f1ca7fdd382df5-9c1732da47eb346b-00` | curl -i |
| T-05 | відновлений seed | `GET /api/incidents?status=Unknown` | 400 Validation Problem Details | 400, allowlist `New, Triaged, InProgress, Resolved, Closed`, traceId `00-a1a9e3c74e67a560a366d9dc3b673d6a-4bbb7ad78599835e-00` | curl -i |
| T-06 | реалізований етап 3, відновлений seed | `GET /api/incidents/severity-summary` | 200; політика "повний перелік рівнів" -> Critical:0, High:1, Medium:1, Low:1, порядок критичності | 200 OK, `[{"severity":"Critical","count":0},{"severity":"High","count":1},{"severity":"Medium","count":1},{"severity":"Low","count":1}]` | curl -i |
| T-07 | реалізовано етап 3, API і клієнт запущено | натиснути кнопку "Показати підсумок" | UI безпечно показує результат; Network підтверджує запит | Network: `GET /api/incidents/severity-summary` -> 200; UI показав 4 рядки ("Груп: 4", Critical/High/Medium/Low) | скриншот UI + DevTools Network |
| T-08 | після контрольованої зміни даних | `--reset-database`, повторити T-02 і T-06 | seed повертає стенд до відомого стану | reset: "Локальні навчальні дані очищено та повторно заповнено seed-значеннями."; повторний T-02: 200 OK, той самий інцидент "Підозрілий лист із вкладенням" (Medium/Triaged); повторний T-06: 200 OK, `[{"severity":"Critical","count":0},{"severity":"High","count":1},{"severity":"Medium","count":1},{"severity":"Low","count":1}]` — ідентично першому запуску | вивід `dotnet run -- --reset-database` + curl -i (×2) |

Автоматизована перевірка (Release, на злитому main): `Build succeeded`, `Test summary: total: 4, failed: 0, succeeded: 4, skipped: 0`.

## 5. Security-сценарій

Не застосовується до ЛР 1: starter navmisno не містить вбудованого вразливого стану (підтверджено `README.md` і `docs/variant-02-a.md` профілю A — навмисно вразливі стани заплановані для ЛР 2, 4 і 5). У ЛР 1 реалізовано лише функціональне розширення `severity-summary` без зміни моделі загроз.

## 6. Висновок

Реалізовано наскрізне розширення `GET /api/incidents/severity-summary` (baseline 501 -> робочий 200) на рівні «добрий»: окремий response DTO без полів entity, query parameter `status` з allowlist-валідацією і 400 при некоректному значенні, чотири HTTP-сценарії (успішний, з фільтром, невалідний фільтр, плюс наявні порожній/некоректний сценарії списку), три стани клієнта (завантаження, порожній результат, безпечна помилка), структуроване журналювання без чутливих даних. Маршрут простежено від кліку в браузері до PostgreSQL і назад трьома незалежними доказами (DevTools Network, пошук коду за назвою, SQL-звірка через psql), зафіксовано чотири межі довіри з конкретними контролями цього маршруту. Після `--reset-database` list-flow і severity-summary відтворюються ідентично (T-08). Усі 4 автоматизовані тести проходять на злитому `main` у Release-конфігурації. Стан зафіксовано тегом `v0.1.0` на commit `7fbedca04f652553651f05e22fee2e9b18514c53`, опубліковано в публічному репозиторії `securelab-bahlei` (гілки `lab/1-system`, `main`, тег `v0.1.0`). Секрети, `.env`, дампи чи логи з credentials у репозиторій не потрапили (перевірено `git diff --staged` перед кожним commit).

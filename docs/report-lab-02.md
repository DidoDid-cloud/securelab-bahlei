# Звіт до лабораторної роботи № 2

## 1. Ідентифікація стану

- Варіант: 2-A «Трекер інцидентів».
- Успадкований базовий тег: `v0.1.0` (base commit `7fbedca04f652553651f05e22fee2e9b18514c53`).
- Встановлений scaffold: `lab-02-start-v1`.
- Робоча гілка: `lab/2-input-sqli`.
- Vulnerable commit: `9f9630df75a0ba48ece5f80c71251fea23635a28` — "Validate incident contract and reproduce controlled SQL injection".
- Fixed commit: `2ded61fe8ca72fb22c3ed7c8d121ed8f929f9c51` — "Parameterize search query and add regression test".
- Фінальний тег: `v0.2.0` (на fixed commit, без злиття в `main`).
- Опубліковано: `github.com/DidoDid-cloud/securelab-bahlei`, гілка `lab/2-input-sqli`, тег `v0.2.0`.

## 2. Змінений маршрут

### 2.1 Контракт створення інциденту

```text
POST /api/incidents { title, description, severity, occurredAtUtc }
  -> Scaffolding/Lab02Endpoints.cs: MapPost("/api/incidents", ...)
    -> серверна валідація: required + межі довжини (title <=160, description <=4000, обидва Trim())
    -> Enum.TryParse<IncidentSeverity>(..., ignoreCase: true) && Enum.IsDefined (400 на "7")
    -> occurredAtUtc: обов'язковий, не в майбутньому більш ніж на 5 хв
    -> cross-field: severity High/Critical -> description(Trim()) >= 40 символів, інакше 400 "description"
    -> предметний конфлікт: AnyAsync(title == title && Status in {New,Triaged,InProgress,Resolved}) -> 409
  -> сервер призначає: Id, OwnerUserId = DbSeeder.AliceId (фіксований demo-контекст), Status = New,
     CreatedAtUtc/UpdatedAtUtc = now (client передати не може — цих полів немає в CreateIncidentRequest)
  -> Presentation/Contracts/IncidentResponses.cs: CreatedIncidentResponse(Id, Title, Severity, Status, OccurredAtUtc, CreatedAtUtc)
  -> 201 Created
```

### 2.2 Пошук інцидентів (SQL injection route)

```text
GET /api/incidents/search?q=...&sortBy=...
  -> Scaffolding/Lab02Endpoints.cs: MapGet("/api/incidents/search", ...)
  -> ДО ВИПРАВЛЕННЯ (vulnerable commit 9f9630d):
     var sql = "SELECT * FROM incidents WHERE title ILIKE '%" + q + "%' OR description ILIKE '%" + q
                + "%' ORDER BY " + order + " LIMIT 50";
     db.Incidents.FromSqlRaw(sql) -- q і sortBy стають частиною SQL-тексту до виконання
  -> ПІСЛЯ ВИПРАВЛЕННЯ (fixed commit 2ded61f):
     escaped = q.Replace("\\","\\\\").Replace("%","\\%").Replace("_","\\_"); pattern = $"%{escaped}%"
     db.Incidents.Where(i => EF.Functions.ILike(i.Title, pattern, "\\") || EF.Functions.ILike(i.Description, pattern, "\\"))
     sortBy: allowlist {null,"","createdAtUtc","severity","status"}, інакше 400;
       severity/status -> явне ранжування (Critical=3..Low=0; New=0..Closed=4), інакше — лексикографічний порядок рядкової колонки
  -> PostgreSQL: параметризований ILIKE ($@pattern, @pattern0) з ESCAPE '\', ORDER BY через LIMIT @p
  -> 200 OK / 400 (invalid sortBy)
```

## 3. Виконані зміни

1. Додано `CreatedIncidentResponse(Id, Title, Severity, Status, OccurredAtUtc, CreatedAtUtc)` у `Presentation/Contracts/IncidentResponses.cs` — окремий response DTO без `OwnerUserId` та інших полів entity.
2. Реалізовано повну серверну валідацію `POST /api/incidents` у `Scaffolding/Lab02Endpoints.cs`: required + межі довжини для `title`/`description`, `Enum.TryParse` + `Enum.IsDefined` для `severity`, перевірка `occurredAtUtc` (не в майбутньому >5 хв), cross-field правило (`High`/`Critical` -> `description` >= 40 симв.), предметний конфлікт за активним `title` (409).
3. Усунено SQL injection у `GET /api/incidents/search`: заміна рядкової конкатенації й `FromSqlRaw` на LINQ-запит з `EF.Functions.ILike` (параметризовано), екранування wildcard-символів `%`/`_`/`\` для literal-substring семантики.
4. Додано allowlist для `sortBy` (`createdAtUtc`, `severity`, `status`; порожнє/відсутнє -> `createdAtUtc`, невідоме -> 400) з явним числовим ранжуванням для `severity` (Critical>High>Medium>Low) і `status` (New>Triaged>InProgress>Resolved>Closed), оскільки обидва поля зберігаються в БД як рядки (`HasConversion<string>()`).
5. Доповнено `tests/SecureLab.Api.Tests/SearchMechanicsTests.cs` трьома тестами: легітимний апостроф (`Search_LegitimateApostrophe_ReturnsMatch`), безпечна поведінка на SQLi-payload (`Search_SqlInjectionAttempt_ReturnsNoRows`), невідомий `sortBy` (`Search_UnknownSortBy_ReturnsBadRequest`).
6. Виконано огляд коду (A-01) на інші raw-SQL точки: знайдено одну (`DatabaseBootstrap.cs:29`, `ExecuteSqlRawAsync` з жорстко закодованим `TRUNCATE`, без зовнішнього вводу) — класифіковано як trusted static SQL, виправлень не потребує.

## 4. Перевірка

| ID | Сценарій | Очікувано | Фактично | Доказ |
|---|---|---|---|---|
| T-01 | Коректне створення (`title` унікальний, valid DTO) | 201, response DTO без зайвих полів | 201 Created, `{"id":"ca01a11b-d75c-45a1-884a-aa0c60efa2e7","title":"Тестовий інцидент СР-01","severity":"Medium","status":"New","occurredAtUtc":"2026-09-26T12:00:00+00:00","createdAtUtc":"2026-09-26T17:18:45..."}` | curl -i |
| T-02 | Некоректний DTO: `severity:"7"`, порожній `title` | 400, application/problem+json | `severity:"7"` -> 400, `errors.severity`: "Допустимі значення: Low, Medium, High, Critical."; порожній `title` -> 400, `errors.title`: "Поле є обов'язковим." | curl -i (×2) |
| T-03 | Предметний конфлікт: повторний POST з тим самим `title` | 409 у Problem Details | 409 Conflict, `"title":"Інцидент із таким title вже активний","detail":"Активний інцидент з title 'Тестовий інцидент СР-01' вже існує."` | curl -i |
| S-01 | SQLi до fix: `q=zz-no-match' OR TRUE -- ` на vulnerable commit `9f9630d` | небажано розширена вибірка | 200 OK, повернуто всі 8 інцидентів з БД (замість очікуваних 0), без сортування за `created_at_utc` — `ORDER BY`/`LIMIT 50` скасовані SQL-коментарем `--` | curl -i + спостереження |
| S-02 | Retest SQLi: той самий payload на fixed commit `2ded61f` | порожній/обмежений за контрактом результат | 200 OK, `[]`; підтверджено також автоматизованим тестом `Search_SqlInjectionAttempt_ReturnsNoRows` | curl -i + test output |
| T-04 | Позитивна регресія: нормальний пошук (`q=USB`) і апостроф (`q=комп'ютерного`) | коректні записи, без 500 | `q=USB` -> 200, 1 запис (обидва рази); `q=комп'ютерного` -> **до fix: 500** (розрив SQL-літерала апострофом), **після fix: 200**, знайдено "Перевірка журналу комп'ютерного класу"; підтверджено тестом `Search_LegitimateApostrophe_ReturnsMatch` | curl -i (×2) + test output |
| T-05 (достатній рівень) | Невідоме сортування: `sortBy=title; DROP TABLE incidents` | 400 | 400 Bad Request, `errors.sortBy`: "Допустимі значення: createdAtUtc, severity, status."; таблиця `incidents` не постраждала (перевірено наступними запитами); підтверджено тестом `Search_UnknownSortBy_ReturnsBadRequest` | curl -i + test output |
| T-06 | Ресурс не знайдено: `GET /api/incidents/99999999-...` | 404 Problem Details | 404 Not Found, `"title":"Інцидент не знайдено","status":404,"detail":"Інцидент '99999999-9999-9999-9999-999999999999' не існує."` | curl -i |
| T-09 | Cross-field 2-A: `severity:"High"` + `description` 39 симв. проти 40 симв. | перший 400 (`description`), другий 200/201 | 39 симв. -> 400, `errors.description`: "Для severity High або Critical опис має містити щонайменше 40 символів."; 40 симв. -> 201 Created | curl -i (×2) |
| A-01 (добрий рівень) | Огляд data-access points: `grep -rn "FromSqlRaw\|ExecuteSqlRaw" src/ tests/` | для кожного збігу — категорія й висновок | Знайдено 1 джерельний збіг: `DatabaseBootstrap.cs:29`, `ExecuteSqlRawAsync("TRUNCATE TABLE ... RESTART IDENTITY CASCADE;")` — жорстко закодований рядок, зовнішній ввід не бере участі → **trusted static SQL**, виправлень не потребує. Інших raw-SQL точок у джерельному коді не знайдено (виправлена точка в `Lab02Endpoints.cs` була єдиною вразливою) | вивід grep |

Автоматизована перевірка (Release, гілка `lab/2-input-sqli`, fixed commit): `Build succeeded`, `Test summary: total: 8, failed: 0, succeeded: 8, skipped: 0`.

*T-10 і A-02 (вимоги відмінного рівня) у цій роботі не виконувались — цілі поточної ітерації обмежені достатнім/добрим рівнем.*

## 5. Security-сценарій

1. **Контекст і гіпотеза.** Локальний endpoint `GET /api/incidents/search` отримує недовірені `q` і `sortBy` з query string; існував ризик, що ці значення потраплять у структуру SQL-запиту до його виконання.
2. **Стан до.** Vulnerable commit `9f9630df75a0ba48ece5f80c71251fea23635a28`. Точка складання SQL: `Scaffolding/Lab02Endpoints.cs`, метод `MapGet("/api/incidents/search", ...)` — рядкова конкатенація `q` і `order` (похідне від `sortBy`) у SQL-текст, переданий у `FromSqlRaw`.
3. **Мінімальний PoC.** Read-only контрольний сценарій: `GET /api/incidents/search?q=zz-no-match%27%20OR%20TRUE%20--%20` на локальному стенді зі штучними даними; жодних `DROP`/`DELETE`/`UPDATE` не виконувалось.
4. **Спостереження.** Замість очікуваних 0 записів (як для `q=zz-no-match` без ін'єкції) повернуто всі 8 наявних інцидентів, без застосованого сортування — `OR TRUE` зробило `WHERE`-умову завжди істинною, а `--` перетворило залишок SQL-рядка (`ORDER BY ... LIMIT 50`) на коментар.
5. **Першопричина.** `q` і `sortBy` ставали частиною SQL-тексту до виконання (через конкатенацію рядків і `FromSqlRaw`), а не передавались як параметри/значення. Фільтрація одного апострофа чи ключового слова `OR` не була б фіксом: атака не потребує апострофа в принципі (`--` після `TRUE` без лапок теж підриває запит), а зона ризику — сам факт побудови SQL-тексту із зовнішнього вводу.
6. **Виправлення.** Пошук переписано на LINQ з `EF.Functions.ILike(column, pattern, "\\")` — значення пошуку передається як параметр СУБД, а не текст запиту; символи `%`, `_`, `\` в введенні користувача екрануються для literal-substring семантики. `sortBy` обробляється через allowlist із явним числовим ранжуванням для `severity`/`status` (обидва зберігаються як рядки в БД, тому потребують явного порядку, а не лексикографічного).
7. **Retest і позитивна регресія.** Той самий PoC на fixed commit `2ded61fe8ca72fb22c3ed7c8d121ed8f929f9c51` повертає `200 OK`, `[]` — SQL-структуру не змінено. Нормальний пошук (`q=USB`) і легітимний апостроф (`q=комп'ютерного`, до fix ламав запит через `500`) після fix працюють коректно (`200`, правильні записи). Підтверджено трьома автоматизованими тестами.
8. **Залишковий ризик.** Ця перевірка не замінює authorization (будь-хто без автентифікації досі бачить усі інциденти — принцип власника з'явиться в ЛР 3), обмеження ресурсу чи пагінацію за межами `LIMIT 50`, перевірку інших endpoint'ів поза цим маршрутом, чи production-політику журналювання.

## 6. Git-інформація

- Робоча гілка: `lab/2-input-sqli` (основна гілка не зачіпалась — тег ставиться на робочу гілку, без злиття в `main`, згідно з методикою ЛР 2).
- Vulnerable commit: `9f9630df75a0ba48ece5f80c71251fea23635a28`.
- Fixed commit: `2ded61fe8ca72fb22c3ed7c8d121ed8f929f9c51`.
- Фінальний тег: `v0.2.0`, `git rev-list -n 1 v0.2.0` = `2ded61fe8ca72fb22c3ed7c8d121ed8f929f9c51` (збігається з fixed commit).
- Опубліковано: `git push -u origin lab/2-input-sqli`, `git push origin v0.2.0` — успішно.

## 7. Підтвердження перегляду diff

Перед кожним із двох commits (`9f9630d`, `2ded61f`) виконано `git status` і `git diff --staged`; підтверджено відсутність `.env`, паролів, cookies, access token, дампів БД чи логів з credentials у складі змін.

## 8. Висновок

Реалізовано серверну валідацію контракту `POST /api/incidents` (окремий request/response DTO, required/length/enum-перевірки, cross-field правило, предметний конфлікт — 409) та усунено SQL injection у `GET /api/incidents/search` на рівні «добрий»: першопричину (рядкова конкатенація недовіреного `q`/`sortBy` у `FromSqlRaw`) усунено через LINQ з `EF.Functions.ILike` (параметризовано) та escape wildcard-символів; `sortBy` захищено allowlist з явним ранжуванням для `severity`/`status` (рядкові колонки в БД). Вразливість відтворено й локалізовано на окремому read-only-контрольованому commit (`9f9630d`), виправлення підтверджено окремим commit (`2ded61f`) із тим самим PoC (S-02) і позитивною регресією (T-04: нормальний пошук і легітимний апостроф). Проведено огляд коду (A-01) на інші raw-SQL точки: знайдено одну, класифіковано як trusted static SQL (жорстко закодований `TRUNCATE`, без зовнішнього вводу), інших вразливих місць не виявлено. Усі 8 автоматизованих тестів проходять у Release-конфігурації на fixed commit. Стан зафіксовано тегом `v0.2.0` на fixed commit `2ded61fe8ca72fb22c3ed7c8d121ed8f929f9c51`, опубліковано в `securelab-bahlei` (гілка `lab/2-input-sqli`, тег `v0.2.0`). Секрети, `.env`, дампи чи логи з credentials у репозиторій не потрапили (перевірено `git diff --staged` перед кожним commit).
using Microsoft.EntityFrameworkCore;
using SecureLab.Api.Data;
using SecureLab.Api.Data.Entities;
using SecureLab.Api.Presentation.Contracts;

namespace SecureLab.Api.Scaffolding;

// Навчальний старт ЛР 02. Запускати лише з локальними штучними даними.
public static class Lab02Endpoints
{
    public static void MapLab02Endpoints(this WebApplication app)
    {
        app.MapGet("/api/incidents/search", async (string? q, string? sortBy, SecureLabDbContext db, CancellationToken ct) =>
        {
            if (sortBy is not (null or "" or "createdAtUtc" or "severity" or "status"))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["sortBy"] = ["Допустимі значення: createdAtUtc, severity, status."]
                });
            }

            // Літеральний substring-пошук: екрануємо саму escape-риску і обидва wildcard ILIKE (%, _).
            var term = q ?? "";
            var escaped = term.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
            var pattern = $"%{escaped}%";

            var query = db.Incidents.AsNoTracking().Where(incident =>
                EF.Functions.ILike(incident.Title, pattern, "\\") ||
                EF.Functions.ILike(incident.Description, pattern, "\\"));

            query = sortBy switch
            {
                "severity" => query.OrderByDescending(incident =>
                    incident.Severity == IncidentSeverity.Critical ? 3
                    : incident.Severity == IncidentSeverity.High ? 2
                    : incident.Severity == IncidentSeverity.Medium ? 1
                    : 0),
                "status" => query.OrderBy(incident =>
                    incident.Status == IncidentStatus.New ? 0
                    : incident.Status == IncidentStatus.Triaged ? 1
                    : incident.Status == IncidentStatus.InProgress ? 2
                    : incident.Status == IncidentStatus.Resolved ? 3
                    : 4),
                _ => query.OrderByDescending(incident => incident.CreatedAtUtc)
            };

            var rows = await query.Take(50).ToListAsync(ct);

            return Results.Ok(rows.Select(row => new
            {
                row.Id, row.Title, row.Description,
                Severity = row.Severity.ToString(), Status = row.Status.ToString(), row.CreatedAtUtc
            }));
        });
        
        app.MapPost("/api/incidents", async (CreateIncidentRequest request, SecureLabDbContext db, CancellationToken ct) =>
        {
            var errors = new Dictionary<string, string[]>();

            var title = request.Title?.Trim() ?? "";
            if (title.Length == 0)
                errors["title"] = ["Поле є обов'язковим."];
            else if (title.Length > 160)
                errors["title"] = ["Довжина title не може перевищувати 160 символів."];

            var description = request.Description?.Trim() ?? "";
            if (description.Length == 0)
                errors["description"] = ["Поле є обов'язковим."];
            else if (description.Length > 4000)
                errors["description"] = ["Довжина description не може перевищувати 4000 символів."];

            if (!Enum.TryParse<IncidentSeverity>(request.Severity, ignoreCase: true, out var severity)
                || !Enum.IsDefined(severity))
            {
                errors["severity"] = ["Допустимі значення: Low, Medium, High, Critical."];
            }

            if (request.OccurredAtUtc is null)
            {
                errors["occurredAtUtc"] = ["Поле є обов'язковим."];
            }
            else if (request.OccurredAtUtc > DateTimeOffset.UtcNow.AddMinutes(5))
            {
                errors["occurredAtUtc"] = ["Дата не може випереджати поточний час сервера більш ніж на 5 хвилин."];
            }

            if (errors.Count == 0
                && (severity == IncidentSeverity.High || severity == IncidentSeverity.Critical)
                && description.Length < 40)
            {
                errors["description"] = ["Для severity High або Critical опис має містити щонайменше 40 символів."];
            }

            if (errors.Count > 0)
            {
                return Results.ValidationProblem(errors);
            }

            var activeStatuses = new[]
            {
                IncidentStatus.New, IncidentStatus.Triaged, IncidentStatus.InProgress, IncidentStatus.Resolved
            };
            var hasConflict = await db.Incidents.AsNoTracking().AnyAsync(
                incident => incident.Title == title && activeStatuses.Contains(incident.Status), ct);
            if (hasConflict)
            {
                return Results.Problem(
                    title: "Інцидент із таким title вже активний",
                    detail: $"Активний інцидент з title '{title}' вже існує.",
                    statusCode: StatusCodes.Status409Conflict);
            }

            var now = DateTimeOffset.UtcNow;
            var incident = new Incident
            {
                Id = Guid.NewGuid(),
                OwnerUserId = DbSeeder.AliceId,
                Title = title,
                Description = description,
                Severity = severity,
                Status = IncidentStatus.New,
                OccurredAtUtc = request.OccurredAtUtc!.Value,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            db.Incidents.Add(incident);
            await db.SaveChangesAsync(ct);

            return Results.Created($"/api/incidents/{incident.Id}", new CreatedIncidentResponse(
                incident.Id, incident.Title, incident.Severity.ToString(), incident.Status.ToString(),
                incident.OccurredAtUtc, incident.CreatedAtUtc));
        });
    }
}

public sealed record CreateIncidentRequest(
    string? Title, string? Description, string? Severity, DateTimeOffset? OccurredAtUtc);

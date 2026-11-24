using Microsoft.EntityFrameworkCore;
using ServiceCenter.Web.Data;
using ServiceCenter.Web.Domain;

namespace ServiceCenter.Web.Services;

/// <summary>
/// Прикладні сценарії роботи із заявками. Усі перевірки прав, станів і введення
/// виконуються тут (довірена частина), а не в інтерфейсі.
/// </summary>
public sealed class RequestService
{
    public const decimal MaxWorkCost = 1_000_000m;
    public const string ConflictMessage =
        "Запис змінено іншим користувачем. Показано актуальні дані; повторіть дію за потреби.";

    private readonly AppDbContext _db;
    private readonly TimeProvider _time;

    public RequestService(AppDbContext db, TimeProvider time)
    {
        _db = db;
        _time = time;
    }

    private DateTime Now => _time.GetUtcNow().UtcDateTime;

    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);

    // ---------------------------------------------------------------- створення

    public async Task<OperationResult> CreateAsync(Actor actor, CreateRequestInput input, CancellationToken ct = default)
    {
        if (actor.Role != UserRole.Client)
        {
            return OperationResult.Fail(FailureKind.Forbidden, "Створювати звернення може лише клієнт.");
        }

        var name = (input.EquipmentName ?? string.Empty).Trim();
        var serial = (input.SerialNumber ?? string.Empty).Trim();
        var fault = (input.FaultDescription ?? string.Empty).Trim();

        var errors = new List<string>();
        if (name.Length is 0 or > 100) errors.Add("Назва обладнання обов'язкова, до 100 символів.");
        if (serial.Length is 0 or > 50) errors.Add("Серійний номер обов'язковий, до 50 символів.");
        if (fault.Length is < 10 or > 2000) errors.Add("Опис несправності: від 10 до 2000 символів.");
        if (errors.Count > 0)
        {
            return OperationResult.Fail(FailureKind.Validation, string.Join(" ", errors));
        }

        var equipment = await _db.EquipmentItems
            .FirstOrDefaultAsync(e => e.OwnerId == actor.Id && e.SerialNumber == serial, ct);

        if (equipment is not null)
        {
            var hasOpen = await _db.Requests.AnyAsync(r => r.EquipmentId == equipment.Id
                && r.Status != RequestStatus.Completed && r.Status != RequestStatus.Cancelled, ct);
            if (hasOpen)
            {
                return OperationResult.Fail(FailureKind.Conflict,
                    "Для цього обладнання вже існує відкрите звернення.");
            }
        }
        else
        {
            equipment = new Equipment { OwnerId = actor.Id, Name = name, SerialNumber = serial };
        }

        var now = Now;
        var request = new ServiceRequest
        {
            ClientId = actor.Id,
            Equipment = equipment,
            FaultDescription = fault,
            SearchText = $"{equipment.Name} {serial} {fault}".ToLowerInvariant(),
            Status = RequestStatus.New,
            CreatedAt = now,
            UpdatedAt = now,
            Version = 1
        };
        request.Events.Add(new RequestEvent
        {
            FromStatus = null,
            ToStatus = RequestStatus.New,
            AuthorId = actor.Id,
            OccurredAt = now,
            Comment = "Звернення створено"
        });
        _db.Requests.Add(request);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            _db.ChangeTracker.Clear();
            return OperationResult.Fail(FailureKind.Conflict,
                "Не вдалося створити звернення: для цього обладнання вже існує відкрите звернення.");
        }

        return OperationResult.Ok(request.Id);
    }

    // ---------------------------------------------------------------- читання

    public async Task<PagedResult<RequestListItem>> ListAsync(Actor actor, RequestFilter filter, CancellationToken ct = default)
    {
        IQueryable<ServiceRequest> q = _db.Requests.AsNoTracking();

        q = actor.Role switch
        {
            UserRole.Client => q.Where(r => r.ClientId == actor.Id),
            UserRole.Technician => q.Where(r => r.AssignedTechnicianId == actor.Id),
            UserRole.Receptionist => q,
            _ => q.Where(r => false)
        };

        if (filter.Status is { } status)
        {
            q = q.Where(r => r.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim().ToLowerInvariant();
            var digits = term.StartsWith("sr-") ? term[3..] : term;
            if (int.TryParse(digits, out var number))
            {
                q = q.Where(r => r.Id == number || r.SearchText.Contains(term));
            }
            else
            {
                q = q.Where(r => r.SearchText.Contains(term));
            }
        }

        var total = await q.CountAsync(ct);
        var pageSize = Math.Clamp(filter.PageSize, 1, 100);
        var totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
        var page = Math.Clamp(filter.Page, 1, totalPages);

        var rows = await q
            .OrderByDescending(r => r.UpdatedAt).ThenByDescending(r => r.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(r => new
            {
                r.Id,
                Equipment = r.Equipment!.Name,
                Client = r.Client!.DisplayName,
                r.Status,
                Technician = r.AssignedTechnician != null ? r.AssignedTechnician.DisplayName : null,
                r.UpdatedAt
            })
            .ToListAsync(ct);

        var items = rows
            .Select(x => new RequestListItem(x.Id, $"SR-{x.Id:D6}", x.Equipment, x.Client, x.Status,
                x.Technician, Utc(x.UpdatedAt)))
            .ToList();

        return new PagedResult<RequestListItem>(items, page, pageSize, total);
    }

    public async Task<RequestDetails?> GetAsync(Actor actor, int id, CancellationToken ct = default)
    {
        var r = await _db.Requests.AsNoTracking()
            .Include(x => x.Client)
            .Include(x => x.Equipment)
            .Include(x => x.AssignedTechnician)
            .Include(x => x.WorkItems)
            .Include(x => x.Events).ThenInclude(e => e.Author)
            .AsSplitQuery()
            .FirstOrDefaultAsync(x => x.Id == id, ct);

        // Чужа заявка не відрізняється від неіснуючої (політика 404).
        if (r is null || !CanSee(actor, r))
        {
            return null;
        }

        var works = r.WorkItems.OrderBy(w => w.Id)
            .Select(w => new WorkItemView(w.Id, w.Title, w.Cost, w.Status)).ToList();
        var events = r.Events.OrderBy(e => e.OccurredAt).ThenBy(e => e.Id)
            .Select(e => new EventView(Utc(e.OccurredAt), e.Author!.DisplayName, e.FromStatus, e.ToStatus, e.Comment))
            .ToList();
        var approvedTotal = r.WorkItems
            .Where(w => w.Status is WorkItemStatus.Approved or WorkItemStatus.Done)
            .Sum(w => w.Cost);

        return new RequestDetails(r.Id, r.Number, r.Client!.DisplayName, r.Equipment!.Name,
            r.Equipment.SerialNumber, r.FaultDescription, r.Status, r.AssignedTechnicianId,
            r.AssignedTechnician?.DisplayName, r.DiagnosisResult, r.ExecutionResult, r.Version,
            Utc(r.CreatedAt), Utc(r.UpdatedAt), works, events, approvedTotal);
    }

    public async Task<IReadOnlyList<TechnicianItem>> ListTechniciansAsync(CancellationToken ct = default)
    {
        return await _db.Users.AsNoTracking()
            .Where(u => u.Role == UserRole.Technician)
            .OrderBy(u => u.DisplayName)
            .Select(u => new TechnicianItem(u.Id, u.DisplayName))
            .ToListAsync(ct);
    }

    // ---------------------------------------------------------------- зміна стану

    public async Task<OperationResult> AssignAsync(Actor actor, int id, int technicianId, int expectedVersion,
        CancellationToken ct = default)
    {
        if (actor.Role != UserRole.Receptionist)
        {
            return OperationResult.Fail(FailureKind.Forbidden, "Призначати виконавця може лише приймальник.");
        }

        var tech = await _db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == technicianId && u.Role == UserRole.Technician, ct);
        if (tech is null)
        {
            return OperationResult.Fail(FailureKind.Validation, "Виконавця не знайдено.");
        }

        return await MutateAsync(actor, id, expectedVersion, (r, now) =>
        {
            var failure = Transition(r, RequestStatus.Assigned, actor, now, $"Призначено виконавця: {tech.DisplayName}");
            if (failure is not null) return failure;
            r.AssignedTechnicianId = tech.Id;
            return null;
        }, ct);
    }

    public async Task<OperationResult> RecordDiagnosisAsync(Actor actor, int id, string diagnosis,
        IReadOnlyList<WorkItemInput> works, int expectedVersion, CancellationToken ct = default)
    {
        if (actor.Role != UserRole.Technician)
        {
            return OperationResult.Fail(FailureKind.Forbidden, "Діагностику фіксує лише виконавець.");
        }

        var text = (diagnosis ?? string.Empty).Trim();
        var errors = ValidateWorks(works);
        if (text.Length is 0 or > 2000)
        {
            errors.Insert(0, "Результат діагностики обов'язковий, до 2000 символів.");
        }
        if (errors.Count > 0)
        {
            return OperationResult.Fail(FailureKind.Validation, string.Join(" ", errors));
        }

        return await MutateAsync(actor, id, expectedVersion, (r, now) =>
        {
            var failure = Transition(r, RequestStatus.Diagnosed, actor, now, "Діагностику виконано, роботи запропоновано");
            if (failure is not null) return failure;
            r.DiagnosisResult = text;
            foreach (var w in works)
            {
                _db.WorkItems.Add(new WorkItem
                {
                    RequestId = r.Id,
                    Title = w.Title.Trim(),
                    Cost = decimal.Round(w.Cost, 2),
                    Status = WorkItemStatus.Proposed
                });
            }
            return null;
        }, ct);
    }

    public async Task<OperationResult> DecideWorksAsync(Actor actor, int id, IReadOnlyCollection<int> approvedIds,
        int expectedVersion, CancellationToken ct = default)
    {
        if (actor.Role != UserRole.Client)
        {
            return OperationResult.Fail(FailureKind.Forbidden, "Перелік робіт погоджує лише клієнт.");
        }

        var ids = (approvedIds ?? Array.Empty<int>()).Distinct().ToHashSet();

        return await MutateAsync(actor, id, expectedVersion, (r, now) =>
        {
            if (r.Status != RequestStatus.Diagnosed)
            {
                return OperationResult.Fail(FailureKind.InvalidState,
                    "Погодження можливе лише для заявки у стані «Очікує погодження робіт».");
            }

            var proposed = r.WorkItems.Where(w => w.Status == WorkItemStatus.Proposed).ToList();
            if (ids.Count == 0)
            {
                return OperationResult.Fail(FailureKind.Validation,
                    "Оберіть принаймні одну роботу або скасуйте звернення.");
            }
            if (!ids.All(i => proposed.Any(w => w.Id == i)))
            {
                return OperationResult.Fail(FailureKind.Validation,
                    "Перелік містить роботи, які не належать цій заявці.");
            }

            var failure = Transition(r, RequestStatus.InProgress, actor, now,
                $"Клієнт погодив робіт: {ids.Count} з {proposed.Count}");
            if (failure is not null) return failure;

            foreach (var w in proposed)
            {
                w.Status = ids.Contains(w.Id) ? WorkItemStatus.Approved : WorkItemStatus.Rejected;
            }
            return null;
        }, ct);
    }

    public async Task<OperationResult> MarkWorkDoneAsync(Actor actor, int id, int workItemId, int expectedVersion,
        CancellationToken ct = default)
    {
        if (actor.Role != UserRole.Technician)
        {
            return OperationResult.Fail(FailureKind.Forbidden, "Виконання робіт фіксує лише виконавець.");
        }

        return await MutateAsync(actor, id, expectedVersion, (r, now) =>
        {
            if (r.Status != RequestStatus.InProgress)
            {
                return OperationResult.Fail(FailureKind.InvalidState,
                    "Роботи виконують лише для заявки у стані «Виконується».");
            }

            var work = r.WorkItems.FirstOrDefault(w => w.Id == workItemId);
            if (work is null)
            {
                return OperationResult.Fail(FailureKind.NotFound, "Роботу не знайдено.");
            }
            if (work.Status == WorkItemStatus.Done)
            {
                return OperationResult.Fail(FailureKind.InvalidState, "Роботу вже виконано.");
            }
            if (work.Status != WorkItemStatus.Approved)
            {
                return OperationResult.Fail(FailureKind.InvalidState, "Не можна виконати непогоджену роботу.");
            }

            work.Status = WorkItemStatus.Done;
            AddEvent(r, r.Status, r.Status, actor, now, $"Роботу «{work.Title}» виконано");
            return null;
        }, ct);
    }

    public async Task<OperationResult> CompleteAsync(Actor actor, int id, string executionResult, int expectedVersion,
        CancellationToken ct = default)
    {
        if (actor.Role != UserRole.Technician)
        {
            return OperationResult.Fail(FailureKind.Forbidden, "Завершувати заявку може лише виконавець.");
        }

        var text = (executionResult ?? string.Empty).Trim();
        if (text.Length is 0 or > 2000)
        {
            return OperationResult.Fail(FailureKind.Validation,
                "Завершення без результату виконання неможливе (до 2000 символів).");
        }

        return await MutateAsync(actor, id, expectedVersion, (r, now) =>
        {
            if (r.Status != RequestStatus.InProgress)
            {
                return OperationResult.Fail(FailureKind.InvalidState,
                    "Завершити можна лише заявку у стані «Виконується».");
            }
            if (r.WorkItems.Any(w => w.Status == WorkItemStatus.Approved))
            {
                return OperationResult.Fail(FailureKind.InvalidState,
                    "Є погоджені, але не виконані роботи.");
            }

            var failure = Transition(r, RequestStatus.Completed, actor, now, "Заявку завершено");
            if (failure is not null) return failure;
            r.ExecutionResult = text;
            return null;
        }, ct);
    }

    public async Task<OperationResult> CancelAsync(Actor actor, int id, string reason, int expectedVersion,
        CancellationToken ct = default)
    {
        if (actor.Role == UserRole.Technician)
        {
            return OperationResult.Fail(FailureKind.Forbidden, "Виконавець не може скасовувати заявки.");
        }

        var text = (reason ?? string.Empty).Trim();
        if (text.Length is 0 or > 500)
        {
            return OperationResult.Fail(FailureKind.Validation, "Причина скасування обов'язкова (до 500 символів).");
        }

        return await MutateAsync(actor, id, expectedVersion, (r, now) =>
        {
            var allowed = actor.Role == UserRole.Client
                ? r.Status is RequestStatus.New or RequestStatus.Diagnosed
                : r.Status is RequestStatus.New or RequestStatus.Assigned or RequestStatus.Diagnosed;
            if (!allowed)
            {
                return OperationResult.Fail(FailureKind.InvalidState,
                    "Скасування в поточному стані неможливе для вашої ролі.");
            }

            return Transition(r, RequestStatus.Cancelled, actor, now, $"Скасовано: {text}");
        }, ct);
    }

    // ---------------------------------------------------------------- допоміжні

    private static bool CanSee(Actor actor, ServiceRequest r) => actor.Role switch
    {
        UserRole.Receptionist => true,
        UserRole.Client => r.ClientId == actor.Id,
        UserRole.Technician => r.AssignedTechnicianId == actor.Id,
        _ => false
    };

    private static List<string> ValidateWorks(IReadOnlyList<WorkItemInput> works)
    {
        var errors = new List<string>();
        if (works is null || works.Count == 0)
        {
            errors.Add("Потрібна принаймні одна робота.");
            return errors;
        }
        if (works.Count > 20)
        {
            errors.Add("Не більше 20 робіт в одній заявці.");
        }

        for (var i = 0; i < works.Count; i++)
        {
            var w = works[i];
            var title = (w.Title ?? string.Empty).Trim();
            if (title.Length is 0 or > 200)
            {
                errors.Add($"Робота {i + 1}: назва обов'язкова, до 200 символів.");
            }
            if (w.Cost <= 0 || w.Cost > MaxWorkCost || decimal.Round(w.Cost, 2) != w.Cost)
            {
                errors.Add($"Робота {i + 1}: вартість має бути більшою за 0, не більшою за {MaxWorkCost:N0} і мати не більше двох знаків після коми.");
            }
        }
        return errors;
    }

    /// <summary>Завантажує агрегат, перевіряє доступ і версію, виконує зміну та зберігає з контролем конкурентності.</summary>
    private async Task<OperationResult> MutateAsync(Actor actor, int id, int expectedVersion,
        Func<ServiceRequest, DateTime, OperationResult?> mutation, CancellationToken ct)
    {
        var request = await _db.Requests.Include(r => r.WorkItems).FirstOrDefaultAsync(r => r.Id == id, ct);
        if (request is null || !CanSee(actor, request))
        {
            return OperationResult.Fail(FailureKind.NotFound, "Звернення не знайдено.");
        }
        if (request.Version != expectedVersion)
        {
            return OperationResult.Fail(FailureKind.Conflict, ConflictMessage);
        }

        var now = Now;
        var failure = mutation(request, now);
        if (failure is not null)
        {
            _db.ChangeTracker.Clear();
            return failure;
        }

        request.Version++;
        request.UpdatedAt = now;

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            _db.ChangeTracker.Clear();
            return OperationResult.Fail(FailureKind.Conflict, ConflictMessage);
        }
        catch (DbUpdateException)
        {
            _db.ChangeTracker.Clear();
            return OperationResult.Fail(FailureKind.Conflict, "Не вдалося зберегти зміни через порушення цілісності даних.");
        }

        return OperationResult.Ok(request.Id);
    }

    private OperationResult? Transition(ServiceRequest r, RequestStatus to, Actor actor, DateTime now, string? comment)
    {
        if (!RequestStateMachine.CanTransition(r.Status, to))
        {
            return OperationResult.Fail(FailureKind.InvalidState,
                $"Перехід зі стану «{r.Status.Title()}» у стан «{to.Title()}» неможливий.");
        }

        AddEvent(r, r.Status, to, actor, now, comment);
        r.Status = to;
        return null;
    }

    private void AddEvent(ServiceRequest r, RequestStatus? from, RequestStatus to, Actor actor, DateTime now, string? comment)
    {
        _db.RequestEvents.Add(new RequestEvent
        {
            RequestId = r.Id,
            FromStatus = from,
            ToStatus = to,
            AuthorId = actor.Id,
            OccurredAt = now,
            Comment = comment
        });
    }
}

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ServiceCenter.Web.Domain;
using ServiceCenter.Web.Models;
using ServiceCenter.Web.Services;

namespace ServiceCenter.Web.Controllers;

[Authorize]
public class RequestsController : Controller
{
    private readonly RequestService _service;

    public RequestsController(RequestService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> Index(string? search, RequestStatus? status, int page = 1,
        CancellationToken ct = default)
    {
        var result = await _service.ListAsync(this.GetActor(), new RequestFilter(search, status, page, 10), ct);
        return View(new RequestListVm(result, search, status));
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id, CancellationToken ct = default)
    {
        var actor = this.GetActor();
        var details = await _service.GetAsync(actor, id, ct);
        if (details is null)
        {
            return NotFoundView();
        }

        IReadOnlyList<TechnicianItem> technicians = actor.Role == UserRole.Receptionist
            ? await _service.ListTechniciansAsync(ct)
            : Array.Empty<TechnicianItem>();
        return View(new RequestDetailsVm(details, actor.Role, technicians));
    }

    // ------------------------------------------------------------ створення

    [HttpGet, Authorize(Roles = nameof(UserRole.Client))]
    public IActionResult Create() => View(new CreateRequestVm());

    [HttpPost, Authorize(Roles = nameof(UserRole.Client))]
    public async Task<IActionResult> Create(CreateRequestVm vm, CancellationToken ct = default)
    {
        if (!ModelState.IsValid)
        {
            return View(vm);
        }

        var result = await _service.CreateAsync(this.GetActor(),
            new CreateRequestInput(vm.EquipmentName, vm.SerialNumber, vm.FaultDescription), ct);
        if (!result.Succeeded)
        {
            // Введені дані зберігаються у формі після помилки.
            ModelState.AddModelError(string.Empty, result.Message ?? "Помилка створення.");
            return View(vm);
        }

        TempData["Success"] = "Звернення зареєстровано.";
        return RedirectToAction(nameof(Details), new { id = result.EntityId });
    }

    // ------------------------------------------------------------ дії над заявкою

    [HttpPost, Authorize(Roles = nameof(UserRole.Receptionist))]
    public async Task<IActionResult> Assign(int id, int technicianId, int version, CancellationToken ct = default)
        => Handle(await _service.AssignAsync(this.GetActor(), id, technicianId, version, ct), id,
            "Виконавця призначено.");

    [HttpGet, Authorize(Roles = nameof(UserRole.Technician))]
    public async Task<IActionResult> Diagnose(int id, CancellationToken ct = default)
    {
        var details = await _service.GetAsync(this.GetActor(), id, ct);
        if (details is null)
        {
            return NotFoundView();
        }
        if (details.Status != RequestStatus.Assigned)
        {
            TempData["Error"] = "Діагностику можна фіксувати лише для призначеної заявки.";
            return RedirectToAction(nameof(Details), new { id });
        }

        return View(new DiagnoseVm { Id = id, Version = details.Version, Number = details.Number });
    }

    [HttpPost, Authorize(Roles = nameof(UserRole.Technician))]
    public async Task<IActionResult> Diagnose(DiagnoseVm vm, CancellationToken ct = default)
    {
        if (!ModelState.IsValid)
        {
            return View(vm);
        }

        // Повністю порожні рядки ігноруємо; частково заповнені — відхиляє сервіс.
        var works = vm.Works
            .Where(w => !string.IsNullOrWhiteSpace(w.Title) || w.Cost.HasValue)
            .Select(w => new WorkItemInput(w.Title ?? string.Empty, w.Cost ?? 0m))
            .ToList();

        var result = await _service.RecordDiagnosisAsync(this.GetActor(), vm.Id, vm.Diagnosis, works,
            vm.Version, ct);
        if (result.Succeeded)
        {
            TempData["Success"] = "Діагностику збережено, перелік робіт передано клієнту.";
            return RedirectToAction(nameof(Details), new { id = vm.Id });
        }
        if (result.Failure is FailureKind.Validation)
        {
            ModelState.AddModelError(string.Empty, result.Message ?? "Некоректні дані.");
            return View(vm);
        }
        return Handle(result, vm.Id, string.Empty);
    }

    [HttpPost, Authorize(Roles = nameof(UserRole.Client))]
    public async Task<IActionResult> Decide(int id, int[] approvedIds, int version, CancellationToken ct = default)
        => Handle(await _service.DecideWorksAsync(this.GetActor(), id, approvedIds, version, ct), id,
            "Роботи погоджено.");

    [HttpPost, Authorize(Roles = nameof(UserRole.Technician))]
    public async Task<IActionResult> WorkDone(int id, int workItemId, int version, CancellationToken ct = default)
        => Handle(await _service.MarkWorkDoneAsync(this.GetActor(), id, workItemId, version, ct), id,
            "Роботу позначено виконаною.");

    [HttpPost, Authorize(Roles = nameof(UserRole.Technician))]
    public async Task<IActionResult> Complete(int id, string executionResult, int version,
        CancellationToken ct = default)
        => Handle(await _service.CompleteAsync(this.GetActor(), id, executionResult, version, ct), id,
            "Заявку завершено.");

    [HttpPost]
    public async Task<IActionResult> Cancel(int id, string reason, int version, CancellationToken ct = default)
        => Handle(await _service.CancelAsync(this.GetActor(), id, reason, version, ct), id,
            "Заявку скасовано.");

    // ------------------------------------------------------------ допоміжні

    private IActionResult Handle(OperationResult result, int id, string successMessage)
    {
        if (result.Succeeded)
        {
            TempData["Success"] = successMessage;
            return RedirectToAction(nameof(Details), new { id });
        }

        switch (result.Failure)
        {
            case FailureKind.NotFound:
                return NotFoundView();
            case FailureKind.Forbidden:
                return Forbid();
            default:
                // Для конфлікту користувач бачить повідомлення й актуальні дані після перенаправлення.
                TempData["Error"] = result.Message;
                return RedirectToAction(nameof(Details), new { id });
        }
    }

    private IActionResult NotFoundView()
    {
        Response.StatusCode = StatusCodes.Status404NotFound;
        return View("NotFound");
    }
}

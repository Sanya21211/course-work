using System.ComponentModel.DataAnnotations;

namespace ServiceCenter.Web.Domain;

public class AppUser
{
    public int Id { get; set; }

    [MaxLength(50)]
    public string Login { get; set; } = string.Empty;

    [MaxLength(100)]
    public string DisplayName { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;

    public UserRole Role { get; set; }
}

public class Equipment
{
    public int Id { get; set; }
    public int OwnerId { get; set; }
    public AppUser? Owner { get; set; }

    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(50)]
    public string SerialNumber { get; set; } = string.Empty;
}

public class ServiceRequest
{
    public int Id { get; set; }

    public int ClientId { get; set; }
    public AppUser? Client { get; set; }

    public int EquipmentId { get; set; }
    public Equipment? Equipment { get; set; }

    [MaxLength(2000)]
    public string FaultDescription { get; set; } = string.Empty;

    /// <summary>Нормалізований (нижній регістр) текст для пошуку.</summary>
    [MaxLength(2300)]
    public string SearchText { get; set; } = string.Empty;

    public RequestStatus Status { get; set; } = RequestStatus.New;

    public int? AssignedTechnicianId { get; set; }
    public AppUser? AssignedTechnician { get; set; }

    [MaxLength(2000)]
    public string? DiagnosisResult { get; set; }

    [MaxLength(2000)]
    public string? ExecutionResult { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    /// <summary>Токен оптимістичної конкурентності: збільшується при кожній зміні.</summary>
    [ConcurrencyCheck]
    public int Version { get; set; }

    public List<WorkItem> WorkItems { get; set; } = new();
    public List<RequestEvent> Events { get; set; } = new();

    public string Number => $"SR-{Id:D6}";
}

public class WorkItem
{
    public int Id { get; set; }
    public int RequestId { get; set; }
    public ServiceRequest? Request { get; set; }

    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    public decimal Cost { get; set; }
    public WorkItemStatus Status { get; set; } = WorkItemStatus.Proposed;
}

public class RequestEvent
{
    public int Id { get; set; }
    public int RequestId { get; set; }
    public ServiceRequest? Request { get; set; }

    public RequestStatus? FromStatus { get; set; }
    public RequestStatus ToStatus { get; set; }

    public int AuthorId { get; set; }
    public AppUser? Author { get; set; }

    public DateTime OccurredAt { get; set; }

    [MaxLength(500)]
    public string? Comment { get; set; }
}

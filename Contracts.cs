using ServiceCenter.Web.Domain;

namespace ServiceCenter.Web.Services;

/// <summary>Особа, яка виконує дію (визначається із перевіреної сесії, а не з форми).</summary>
public record Actor(int Id, UserRole Role);

public enum FailureKind
{
    None,
    NotFound,
    Forbidden,
    Validation,
    Conflict,
    InvalidState
}

public record OperationResult(bool Succeeded, FailureKind Failure, string? Message, int? EntityId = null)
{
    public static OperationResult Ok(int? entityId = null) => new(true, FailureKind.None, null, entityId);

    public static OperationResult Fail(FailureKind kind, string message) => new(false, kind, message);
}

public record CreateRequestInput(string EquipmentName, string SerialNumber, string FaultDescription);

public record WorkItemInput(string Title, decimal Cost);

public record RequestFilter(string? Search, RequestStatus? Status, int Page = 1, int PageSize = 10);

public record TechnicianItem(int Id, string Name);

public record RequestListItem(
    int Id, string Number, string EquipmentName, string ClientName,
    RequestStatus Status, string? TechnicianName, DateTime UpdatedAt);

public record WorkItemView(int Id, string Title, decimal Cost, WorkItemStatus Status);

public record EventView(DateTime OccurredAt, string AuthorName, RequestStatus? From, RequestStatus To, string? Comment);

public record RequestDetails(
    int Id, string Number, string ClientName, string EquipmentName, string SerialNumber,
    string FaultDescription, RequestStatus Status, int? TechnicianId, string? TechnicianName,
    string? DiagnosisResult, string? ExecutionResult, int Version,
    DateTime CreatedAt, DateTime UpdatedAt,
    IReadOnlyList<WorkItemView> Works, IReadOnlyList<EventView> Events, decimal ApprovedTotal);

public record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));
}

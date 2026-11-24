namespace ServiceCenter.Web.Domain;

/// <summary>Таблиця допустимих переходів між станами заявки (єдине джерело правди).</summary>
public static class RequestStateMachine
{
    private static readonly Dictionary<RequestStatus, RequestStatus[]> Allowed = new()
    {
        [RequestStatus.New] = new[] { RequestStatus.Assigned, RequestStatus.Cancelled },
        [RequestStatus.Assigned] = new[] { RequestStatus.Assigned, RequestStatus.Diagnosed, RequestStatus.Cancelled },
        [RequestStatus.Diagnosed] = new[] { RequestStatus.InProgress, RequestStatus.Cancelled },
        [RequestStatus.InProgress] = new[] { RequestStatus.Completed },
        [RequestStatus.Completed] = Array.Empty<RequestStatus>(),
        [RequestStatus.Cancelled] = Array.Empty<RequestStatus>()
    };

    public static bool CanTransition(RequestStatus from, RequestStatus to)
        => Allowed.TryGetValue(from, out var targets) && targets.Contains(to);

    public static bool IsOpen(RequestStatus status)
        => status != RequestStatus.Completed && status != RequestStatus.Cancelled;
}

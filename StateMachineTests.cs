using ServiceCenter.Web.Domain;

namespace ServiceCenter.Tests;

public class StateMachineTests
{
    [Theory] // SM-01
    [InlineData(RequestStatus.New, RequestStatus.Assigned)]
    [InlineData(RequestStatus.New, RequestStatus.Cancelled)]
    [InlineData(RequestStatus.Assigned, RequestStatus.Diagnosed)]
    [InlineData(RequestStatus.Diagnosed, RequestStatus.InProgress)]
    [InlineData(RequestStatus.InProgress, RequestStatus.Completed)]
    public void AllowedTransitions_AreAccepted(RequestStatus from, RequestStatus to)
        => Assert.True(RequestStateMachine.CanTransition(from, to));

    [Theory] // SM-02
    [InlineData(RequestStatus.New, RequestStatus.Completed)]
    [InlineData(RequestStatus.New, RequestStatus.Diagnosed)]
    [InlineData(RequestStatus.Diagnosed, RequestStatus.Completed)]
    [InlineData(RequestStatus.InProgress, RequestStatus.Cancelled)]
    [InlineData(RequestStatus.Completed, RequestStatus.InProgress)]
    [InlineData(RequestStatus.Cancelled, RequestStatus.New)]
    public void ForbiddenTransitions_AreRejected(RequestStatus from, RequestStatus to)
        => Assert.False(RequestStateMachine.CanTransition(from, to));
}

using Microsoft.EntityFrameworkCore;
using ServiceCenter.Web.Domain;
using ServiceCenter.Web.Services;

namespace ServiceCenter.Tests;

public class RequestServiceTests
{
    private static readonly WorkItemInput WorkA = new("Заміна роз'єму живлення", 850.00m);
    private static readonly WorkItemInput WorkB = new("Чистка від пилу", 300.00m);

    // ------------------------------------------------------------ створення

    [Fact] // TC-CREATE-01
    public async Task Create_ValidInput_CreatesOneRequestWithHistory()
    {
        using var db = new TestDb();
        var id = await db.CreateRequestAsync(db.ClientA);

        var details = await db.DetailsAsync(db.ClientA, id);
        Assert.Equal(RequestStatus.New, details.Status);
        Assert.Single(details.Events);
        Assert.Equal("clientA", details.Events[0].AuthorName);
    }

    [Fact] // TC-CREATE-02
    public async Task Create_ShortFaultDescription_ReturnsValidationError()
    {
        using var db = new TestDb();
        var result = await db.Svc().CreateAsync(db.ClientA, new CreateRequestInput("Ноутбук", "SN-1", "коротко"));

        Assert.False(result.Succeeded);
        Assert.Equal(FailureKind.Validation, result.Failure);
    }

    [Fact] // TC-CREATE-03
    public async Task Create_SecondOpenRequestForSameEquipment_ReturnsConflict()
    {
        using var db = new TestDb();
        await db.CreateRequestAsync(db.ClientA, "SN-DUP");

        var second = await db.Svc().CreateAsync(db.ClientA,
            new CreateRequestInput("Ноутбук", "SN-DUP", "Повторне надсилання форми"));

        Assert.Equal(FailureKind.Conflict, second.Failure);
        var list = await db.Svc().ListAsync(db.ClientA, new RequestFilter(null, null));
        Assert.Equal(1, list.TotalCount);
    }

    [Fact] // TC-ROLE-01
    public async Task Create_ByNonClient_IsForbidden()
    {
        using var db = new TestDb();
        var result = await db.Svc().CreateAsync(db.Reception,
            new CreateRequestInput("Ноутбук", "SN-1", "Опис несправності обладнання"));

        Assert.Equal(FailureKind.Forbidden, result.Failure);
    }

    // ------------------------------------------------------------ доступ

    [Fact] // TC-ACCESS-01
    public async Task Client_SeesOnlyOwnRequests()
    {
        using var db = new TestDb();
        var own = await db.CreateRequestAsync(db.ClientA, "SN-A");
        var foreign = await db.CreateRequestAsync(db.ClientB, "SN-B");

        var list = await db.Svc().ListAsync(db.ClientA, new RequestFilter(null, null));
        Assert.Single(list.Items);
        Assert.Equal(own, list.Items[0].Id);
        Assert.Null(await db.Svc().GetAsync(db.ClientA, foreign));
    }

    [Fact] // TC-ACCESS-02
    public async Task Client_CannotCancelForeignRequest()
    {
        using var db = new TestDb();
        var id = await db.CreateRequestAsync(db.ClientA);
        var v = await db.VersionAsync(db.Reception, id);

        var result = await db.Svc().CancelAsync(db.ClientB, id, "Спроба чужого скасування", v);

        Assert.Equal(FailureKind.NotFound, result.Failure);
        Assert.Equal(RequestStatus.New, (await db.DetailsAsync(db.Reception, id)).Status);
    }

    [Fact] // TC-ACCESS-03
    public async Task Technician_SeesOnlyAssignedRequests()
    {
        using var db = new TestDb();
        var id = await db.CreateRequestAsync(db.ClientA);
        await db.AssignAsync(id, db.Tech1);

        Assert.NotNull(await db.Svc().GetAsync(db.Tech1, id));
        Assert.Null(await db.Svc().GetAsync(db.Tech2, id));
        Assert.Empty((await db.Svc().ListAsync(db.Tech2, new RequestFilter(null, null))).Items);
    }

    [Fact] // TC-ROLE-02
    public async Task Receptionist_CannotRecordDiagnosis()
    {
        using var db = new TestDb();
        var id = await db.CreateRequestAsync(db.ClientA);
        await db.AssignAsync(id, db.Tech1);
        var v = await db.VersionAsync(db.Reception, id);

        var result = await db.Svc().RecordDiagnosisAsync(db.Reception, id, "Діагноз", new[] { WorkA }, v);

        Assert.Equal(FailureKind.Forbidden, result.Failure);
    }

    // ------------------------------------------------------------ бізнес-правила

    [Fact] // TC-FLOW-01
    public async Task FullFlow_ReachesCompleted_WithAuthorsAndTimesInHistory()
    {
        using var db = new TestDb();
        var id = await db.CreateRequestAsync(db.ClientA);
        await db.AssignAsync(id, db.Tech1);
        await db.DiagnoseAsync(id, db.Tech1, WorkA, WorkB);

        var d = await db.DetailsAsync(db.ClientA, id);
        var decide = await db.Svc().DecideWorksAsync(db.ClientA, id, new[] { d.Works[0].Id }, d.Version);
        Assert.True(decide.Succeeded, decide.Message);

        d = await db.DetailsAsync(db.Tech1, id);
        var done = await db.Svc().MarkWorkDoneAsync(db.Tech1, id, d.Works[0].Id, d.Version);
        Assert.True(done.Succeeded, done.Message);

        var v = await db.VersionAsync(db.Tech1, id);
        var complete = await db.Svc().CompleteAsync(db.Tech1, id, "Роз'єм замінено, пристрій працює", v);
        Assert.True(complete.Succeeded, complete.Message);

        d = await db.DetailsAsync(db.Reception, id);
        Assert.Equal(RequestStatus.Completed, d.Status);
        Assert.Equal(850.00m, d.ApprovedTotal);
        Assert.Equal(WorkItemStatus.Rejected, d.Works[1].Status);
        Assert.All(d.Events, e => Assert.False(string.IsNullOrEmpty(e.AuthorName)));
        Assert.True(d.Events.Count >= 6);
    }

    [Fact] // TC-RULE-01
    public async Task MarkWorkDone_ForRejectedWork_IsRefused()
    {
        using var db = new TestDb();
        var id = await db.CreateRequestAsync(db.ClientA);
        await db.AssignAsync(id, db.Tech1);
        await db.DiagnoseAsync(id, db.Tech1, WorkA, WorkB);
        var d = await db.DetailsAsync(db.ClientA, id);
        await db.Svc().DecideWorksAsync(db.ClientA, id, new[] { d.Works[0].Id }, d.Version);

        d = await db.DetailsAsync(db.Tech1, id);
        var result = await db.Svc().MarkWorkDoneAsync(db.Tech1, id, d.Works[1].Id, d.Version);

        Assert.Equal(FailureKind.InvalidState, result.Failure);
        Assert.Equal(WorkItemStatus.Rejected, (await db.DetailsAsync(db.Tech1, id)).Works[1].Status);
    }

    [Fact] // TC-RULE-02
    public async Task Complete_WithoutExecutionResult_IsRefused()
    {
        using var db = new TestDb();
        var id = await db.CreateRequestAsync(db.ClientA);
        await db.AssignAsync(id, db.Tech1);
        await db.DiagnoseAsync(id, db.Tech1, WorkA);
        var d = await db.DetailsAsync(db.ClientA, id);
        await db.Svc().DecideWorksAsync(db.ClientA, id, new[] { d.Works[0].Id }, d.Version);
        d = await db.DetailsAsync(db.Tech1, id);
        await db.Svc().MarkWorkDoneAsync(db.Tech1, id, d.Works[0].Id, d.Version);

        var v = await db.VersionAsync(db.Tech1, id);
        var result = await db.Svc().CompleteAsync(db.Tech1, id, "   ", v);

        Assert.Equal(FailureKind.Validation, result.Failure);
        Assert.Equal(RequestStatus.InProgress, (await db.DetailsAsync(db.Tech1, id)).Status);
    }

    [Fact] // TC-RULE-03
    public async Task Complete_WithApprovedButUnfinishedWork_IsRefused()
    {
        using var db = new TestDb();
        var id = await db.CreateRequestAsync(db.ClientA);
        await db.AssignAsync(id, db.Tech1);
        await db.DiagnoseAsync(id, db.Tech1, WorkA);
        var d = await db.DetailsAsync(db.ClientA, id);
        await db.Svc().DecideWorksAsync(db.ClientA, id, new[] { d.Works[0].Id }, d.Version);

        var v = await db.VersionAsync(db.Tech1, id);
        var result = await db.Svc().CompleteAsync(db.Tech1, id, "Результат", v);

        Assert.Equal(FailureKind.InvalidState, result.Failure);
    }

    [Theory] // TC-RULE-04
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("1000000.01")]
    [InlineData("10.999")]
    public async Task Diagnosis_WithInvalidCost_ReturnsValidationError(string cost)
    {
        using var db = new TestDb();
        var id = await db.CreateRequestAsync(db.ClientA);
        await db.AssignAsync(id, db.Tech1);
        var v = await db.VersionAsync(db.Tech1, id);
        var value = decimal.Parse(cost, System.Globalization.CultureInfo.InvariantCulture);

        var result = await db.Svc().RecordDiagnosisAsync(db.Tech1, id, "Діагноз",
            new[] { new WorkItemInput("Робота", value) }, v);

        Assert.Equal(FailureKind.Validation, result.Failure);
        Assert.Empty((await db.DetailsAsync(db.Tech1, id)).Works);
    }

    [Fact] // TC-RULE-05
    public async Task Decide_WithoutSelection_IsRefused()
    {
        using var db = new TestDb();
        var id = await db.CreateRequestAsync(db.ClientA);
        await db.AssignAsync(id, db.Tech1);
        await db.DiagnoseAsync(id, db.Tech1, WorkA);
        var v = await db.VersionAsync(db.ClientA, id);

        var result = await db.Svc().DecideWorksAsync(db.ClientA, id, Array.Empty<int>(), v);

        Assert.Equal(FailureKind.Validation, result.Failure);
    }

    [Fact] // TC-RULE-06
    public async Task Cancel_InProgress_IsRefused()
    {
        using var db = new TestDb();
        var id = await db.CreateRequestAsync(db.ClientA);
        await db.AssignAsync(id, db.Tech1);
        await db.DiagnoseAsync(id, db.Tech1, WorkA);
        var d = await db.DetailsAsync(db.ClientA, id);
        await db.Svc().DecideWorksAsync(db.ClientA, id, new[] { d.Works[0].Id }, d.Version);

        var v = await db.VersionAsync(db.ClientA, id);
        var result = await db.Svc().CancelAsync(db.ClientA, id, "Передумав", v);

        Assert.Equal(FailureKind.InvalidState, result.Failure);
    }

    // ------------------------------------------------------------ конкурентність

    [Fact] // TC-CONC-01
    public async Task Assign_WithStaleVersion_ReturnsConflictAndKeepsFirstChange()
    {
        using var db = new TestDb();
        var id = await db.CreateRequestAsync(db.ClientA);
        var staleVersion = await db.VersionAsync(db.Reception, id);

        var first = await db.Svc().AssignAsync(db.Reception, id, db.Tech1.Id, staleVersion);
        var second = await db.Svc().AssignAsync(db.Reception, id, db.Tech2.Id, staleVersion);

        Assert.True(first.Succeeded);
        Assert.Equal(FailureKind.Conflict, second.Failure);
        Assert.Equal(db.Tech1.Id, (await db.DetailsAsync(db.Reception, id)).TechnicianId);
    }

    [Fact] // TC-CONC-02
    public async Task ConcurrencyToken_RejectsStaleUpdateAtDatabaseLevel()
    {
        using var db = new TestDb();
        var id = await db.CreateRequestAsync(db.ClientA);

        await using var ctx1 = db.CreateContext();
        await using var ctx2 = db.CreateContext();
        var r1 = await ctx1.Requests.FirstAsync(r => r.Id == id);
        var r2 = await ctx2.Requests.FirstAsync(r => r.Id == id);

        r1.FaultDescription = "Зміна першого працівника";
        r1.Version++;
        await ctx1.SaveChangesAsync();

        r2.FaultDescription = "Зміна другого працівника";
        r2.Version++;
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => ctx2.SaveChangesAsync());
    }

    // ------------------------------------------------------------ пошук і пагінація

    [Fact] // TC-SEARCH-01
    public async Task Search_IsCaseInsensitiveForCyrillic_AndPagingIsStable()
    {
        using var db = new TestDb();
        for (var i = 0; i < 12; i++)
        {
            await db.CreateRequestAsync(db.ClientA, $"SN-{i:D3}");
        }

        var all = await db.Svc().ListAsync(db.ClientA, new RequestFilter("НОУТБУК", null, 1, 5));
        Assert.Equal(12, all.TotalCount);
        Assert.Equal(5, all.Items.Count);
        Assert.Equal(3, all.TotalPages);

        var none = await db.Svc().ListAsync(db.ClientA, new RequestFilter("принтер", null));
        Assert.Equal(0, none.TotalCount);
    }
}

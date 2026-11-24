using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ServiceCenter.Web.Data;
using ServiceCenter.Web.Domain;
using ServiceCenter.Web.Services;

namespace ServiceCenter.Tests;

/// <summary>Ізольована база SQLite в пам'яті з реальною схемою та обмеженнями.</summary>
public sealed class TestDb : IDisposable
{
    private readonly SqliteConnection _connection;

    public Actor ClientA { get; }
    public Actor ClientB { get; }
    public Actor Reception { get; }
    public Actor Tech1 { get; }
    public Actor Tech2 { get; }

    public TestDb()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        using var ctx = CreateContext();
        ctx.Database.EnsureCreated();

        AppUser Add(string login, UserRole role)
        {
            var user = new AppUser { Login = login, DisplayName = login, PasswordHash = "x", Role = role };
            ctx.Users.Add(user);
            return user;
        }

        var a = Add("clientA", UserRole.Client);
        var b = Add("clientB", UserRole.Client);
        var r = Add("reception", UserRole.Receptionist);
        var t1 = Add("tech1", UserRole.Technician);
        var t2 = Add("tech2", UserRole.Technician);
        ctx.SaveChanges();

        ClientA = new Actor(a.Id, UserRole.Client);
        ClientB = new Actor(b.Id, UserRole.Client);
        Reception = new Actor(r.Id, UserRole.Receptionist);
        Tech1 = new Actor(t1.Id, UserRole.Technician);
        Tech2 = new Actor(t2.Id, UserRole.Technician);
    }

    public AppDbContext CreateContext()
        => new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);

    /// <summary>Новий сервіс із новим контекстом: імітує окремий HTTP-запит.</summary>
    public RequestService Svc() => new(CreateContext(), TimeProvider.System);

    public async Task<int> VersionAsync(Actor actor, int id)
        => (await Svc().GetAsync(actor, id))!.Version;

    public async Task<int> CreateRequestAsync(Actor client, string serial = "SN-001")
    {
        var result = await Svc().CreateAsync(client,
            new CreateRequestInput("Ноутбук", serial, "Не вмикається після падіння"));
        Assert.True(result.Succeeded, result.Message);
        return result.EntityId!.Value;
    }

    public async Task AssignAsync(int id, Actor tech)
    {
        var v = await VersionAsync(Reception, id);
        var result = await Svc().AssignAsync(Reception, id, tech.Id, v);
        Assert.True(result.Succeeded, result.Message);
    }

    public async Task DiagnoseAsync(int id, Actor tech, params WorkItemInput[] works)
    {
        var v = await VersionAsync(tech, id);
        var result = await Svc().RecordDiagnosisAsync(tech, id, "Несправний роз'єм живлення", works, v);
        Assert.True(result.Succeeded, result.Message);
    }

    public async Task<RequestDetails> DetailsAsync(Actor actor, int id) => (await Svc().GetAsync(actor, id))!;

    public void Dispose() => _connection.Dispose();
}

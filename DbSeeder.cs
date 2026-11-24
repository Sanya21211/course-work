using Microsoft.AspNetCore.Identity;
using ServiceCenter.Web.Domain;

namespace ServiceCenter.Web.Data;

/// <summary>Повторюване наповнення синтетичними демонстраційними даними.</summary>
public static class DbSeeder
{
    public const string DemoPassword = "Demo-Password-2026!";

    public static void Seed(AppDbContext db, int volumeRequests = 0)
    {
        if (db.Users.Any())
        {
            return;
        }

        var hasher = new PasswordHasher<AppUser>();

        AppUser Make(string login, string name, UserRole role)
        {
            var user = new AppUser { Login = login, DisplayName = name, Role = role };
            user.PasswordHash = hasher.HashPassword(user, DemoPassword);
            return user;
        }

        var clients = new[]
        {
            Make("client1", "Олена Коваленко", UserRole.Client),
            Make("client2", "Андрій Мельник", UserRole.Client)
        };
        var reception = Make("reception1", "Ірина Шевченко", UserRole.Receptionist);
        var techs = new[]
        {
            Make("tech1", "Максим Бондаренко", UserRole.Technician),
            Make("tech2", "Світлана Ткаченко", UserRole.Technician)
        };

        db.Users.AddRange(clients);
        db.Users.Add(reception);
        db.Users.AddRange(techs);
        db.SaveChanges();

        if (volumeRequests > 0)
        {
            SeedVolume(db, clients, techs, volumeRequests);
        }
    }

    private static void SeedVolume(AppDbContext db, AppUser[] clients, AppUser[] techs, int count)
    {
        var rnd = new Random(42);
        var names = new[] { "Ноутбук", "Смартфон", "Принтер", "Монітор", "Планшет", "Роутер" };
        var faults = new[]
        {
            "Не вмикається після падіння",
            "Швидко розряджається акумулятор",
            "Не працює зарядний роз'єм",
            "Перегрівається під навантаженням",
            "Не відображає зображення"
        };
        var statuses = Enum.GetValues<RequestStatus>();
        var start = new DateTime(2026, 1, 1, 8, 0, 0, DateTimeKind.Utc);

        for (var i = 1; i <= count; i++)
        {
            var client = clients[rnd.Next(clients.Length)];
            var status = statuses[rnd.Next(statuses.Length)];
            var name = names[rnd.Next(names.Length)];
            var serial = $"SN-{i:D6}";
            var fault = $"{faults[rnd.Next(faults.Length)]} (синтетичний запис {i})";
            var created = start.AddMinutes(i * 7L);
            var hasTech = status != RequestStatus.New && status != RequestStatus.Cancelled;

            var request = new ServiceRequest
            {
                ClientId = client.Id,
                Equipment = new Equipment { OwnerId = client.Id, Name = name, SerialNumber = serial },
                FaultDescription = fault,
                SearchText = $"{name} {serial} {fault}".ToLowerInvariant(),
                Status = status,
                AssignedTechnicianId = hasTech ? techs[rnd.Next(techs.Length)].Id : null,
                DiagnosisResult = status is RequestStatus.Diagnosed or RequestStatus.InProgress or RequestStatus.Completed
                    ? "Синтетичний результат діагностики" : null,
                ExecutionResult = status == RequestStatus.Completed ? "Синтетичний результат виконання" : null,
                CreatedAt = created,
                UpdatedAt = created.AddHours(rnd.Next(1, 72)),
                Version = 1
            };
            request.Events.Add(new RequestEvent
            {
                ToStatus = RequestStatus.New,
                AuthorId = client.Id,
                OccurredAt = created,
                Comment = "Звернення створено"
            });
            db.Requests.Add(request);

            if (i % 500 == 0)
            {
                db.SaveChanges();
                db.ChangeTracker.Clear();
            }
        }

        db.SaveChanges();
        db.ChangeTracker.Clear();
    }
}

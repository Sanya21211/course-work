using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ServiceCenter.Web.Data;
using ServiceCenter.Web.Domain;

namespace ServiceCenter.Web.Services;

/// <summary>Автентифікація та реєстрація клієнтів. Паролі хешуються засобами платформи (PBKDF2).</summary>
public sealed class AccountService
{
    public const int MinPasswordLength = 15;
    public const int MaxPasswordLength = 128;

    private static readonly Regex LoginPattern = new("^[a-z0-9._-]{3,50}$", RegexOptions.Compiled);
    private static readonly AppUser DummyUser = new();
    private static readonly string DummyHash =
        new PasswordHasher<AppUser>().HashPassword(DummyUser, "dummy-password-for-timing-equalisation");

    private readonly AppDbContext _db;
    private readonly PasswordHasher<AppUser> _hasher = new();

    public AccountService(AppDbContext db) => _db = db;

    public async Task<AppUser?> ValidateCredentialsAsync(string login, string password, CancellationToken ct = default)
    {
        var normalized = (login ?? string.Empty).Trim().ToLowerInvariant();
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Login == normalized, ct);

        if (user is null)
        {
            // Вирівнювання часу відповіді, щоб не розкривати існування логіна.
            _hasher.VerifyHashedPassword(DummyUser, DummyHash, password ?? string.Empty);
            return null;
        }

        var result = _hasher.VerifyHashedPassword(user, user.PasswordHash, password ?? string.Empty);
        if (result == PasswordVerificationResult.Failed)
        {
            return null;
        }
        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = _hasher.HashPassword(user, password!);
            await _db.SaveChangesAsync(ct);
        }
        return user;
    }

    public async Task<OperationResult> RegisterClientAsync(string login, string displayName, string password,
        CancellationToken ct = default)
    {
        var normalized = (login ?? string.Empty).Trim().ToLowerInvariant();
        var name = (displayName ?? string.Empty).Trim();
        var errors = new List<string>();

        if (!LoginPattern.IsMatch(normalized))
            errors.Add("Логін: 3–50 символів (латинські літери, цифри, . _ -).");
        if (name.Length is 0 or > 100)
            errors.Add("Ім'я обов'язкове, до 100 символів.");
        if (password is null || password.Length < MinPasswordLength || password.Length > MaxPasswordLength)
            errors.Add($"Пароль: від {MinPasswordLength} до {MaxPasswordLength} символів.");
        if (errors.Count > 0)
        {
            return OperationResult.Fail(FailureKind.Validation, string.Join(" ", errors));
        }

        if (await _db.Users.AnyAsync(u => u.Login == normalized, ct))
        {
            return OperationResult.Fail(FailureKind.Conflict, "Логін уже зайнято.");
        }

        var user = new AppUser { Login = normalized, DisplayName = name, Role = UserRole.Client };
        user.PasswordHash = _hasher.HashPassword(user, password!);
        _db.Users.Add(user);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            return OperationResult.Fail(FailureKind.Conflict, "Логін уже зайнято.");
        }
        return OperationResult.Ok(user.Id);
    }
}

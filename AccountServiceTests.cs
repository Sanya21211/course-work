using ServiceCenter.Web.Services;

namespace ServiceCenter.Tests;

public class AccountServiceTests
{
    private const string GoodPassword = "correct-horse-battery-staple";

    [Fact] // TC-AUTH-01
    public async Task Register_ThenLogin_Succeeds_AndHashIsNotPlainText()
    {
        using var db = new TestDb();
        var accounts = new AccountService(db.CreateContext());

        var reg = await accounts.RegisterClientAsync("new.client", "Новий Клієнт", GoodPassword);
        Assert.True(reg.Succeeded, reg.Message);

        var user = await new AccountService(db.CreateContext()).ValidateCredentialsAsync("NEW.client", GoodPassword);
        Assert.NotNull(user);
        Assert.NotEqual(GoodPassword, user!.PasswordHash);
    }

    [Fact] // TC-AUTH-02
    public async Task Login_WithWrongPassword_ReturnsNull()
    {
        using var db = new TestDb();
        var accounts = new AccountService(db.CreateContext());
        await accounts.RegisterClientAsync("new.client", "Новий Клієнт", GoodPassword);

        Assert.Null(await accounts.ValidateCredentialsAsync("new.client", "wrong-password-value"));
        Assert.Null(await accounts.ValidateCredentialsAsync("unknown", GoodPassword));
    }

    [Theory] // TC-AUTH-03
    [InlineData("short")]
    [InlineData("14-chars-only!")]
    public async Task Register_WithShortPassword_IsRefused(string password)
    {
        using var db = new TestDb();
        var result = await new AccountService(db.CreateContext())
            .RegisterClientAsync("new.client", "Новий Клієнт", password);

        Assert.Equal(FailureKind.Validation, result.Failure);
    }

    [Fact] // TC-AUTH-04
    public async Task Register_DuplicateLogin_ReturnsConflict()
    {
        using var db = new TestDb();
        var accounts = new AccountService(db.CreateContext());
        await accounts.RegisterClientAsync("dup", "Один", GoodPassword);

        var second = await new AccountService(db.CreateContext()).RegisterClientAsync("DUP", "Два", GoodPassword);

        Assert.Equal(FailureKind.Conflict, second.Failure);
    }
}

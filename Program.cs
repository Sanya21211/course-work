using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ServiceCenter.Web.Data;
using ServiceCenter.Web.Services;

var culture = new CultureInfo("uk-UA");
CultureInfo.DefaultThreadCurrentCulture = culture;
CultureInfo.DefaultThreadCurrentUICulture = culture;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Default") ?? "Data Source=servicecenter.db"));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<RequestService>();
builder.Services.AddScoped<AccountService>();

builder.Services.AddControllersWithViews(options =>
{
    // CSRF-захист для всіх небезпечних методів.
    options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());

    var messages = options.ModelBindingMessageProvider;
    messages.SetValueMustBeANumberAccessor(field => $"Поле «{field}» має бути числом.");
    messages.SetAttemptedValueIsInvalidAccessor((value, field) => $"Значення «{value}» некоректне для поля «{field}».");
    messages.SetValueIsInvalidAccessor(value => $"Значення «{value}» некоректне.");
    messages.SetUnknownValueIsInvalidAccessor(field => $"Некоректне значення поля «{field}».");
    messages.SetValueMustNotBeNullAccessor(field => $"Поле «{field}» обов'язкове.");
});

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.AccessDeniedPath = "/Account/Denied";
        options.Cookie.Name = "sc.auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
    });
builder.Services.AddAuthorization();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("login", httpContext => RateLimitPartition.GetFixedWindowLimiter(
        httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        }));
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
    DbSeeder.Seed(db, builder.Configuration.GetValue<int>("Seed:VolumeRequests"));
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error");
    app.UseHsts();
}

app.UseHttpsRedirection();

app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "same-origin";
    context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; frame-ancestors 'none'";
    await next();
});

app.UseStaticFiles();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

// Помилка без стека викликів у форматі Problem Details (RFC 9457).
app.Map("/error", () => Results.Problem(title: "Сталася внутрішня помилка.", statusCode: 500));

// Готовність: перевіряє не лише процес, а й доступність бази даних.
app.MapGet("/health", async (AppDbContext db) =>
    await db.Database.CanConnectAsync()
        ? Results.Ok(new { status = "ready" })
        : Results.StatusCode(StatusCodes.Status503ServiceUnavailable));

app.MapControllerRoute(name: "default", pattern: "{controller=Requests}/{action=Index}/{id?}");

app.Run();

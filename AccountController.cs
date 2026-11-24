using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ServiceCenter.Web.Domain;
using ServiceCenter.Web.Models;
using ServiceCenter.Web.Services;

namespace ServiceCenter.Web.Controllers;

public class AccountController : Controller
{
    private readonly AccountService _accounts;

    public AccountController(AccountService accounts) => _accounts = accounts;

    [HttpGet, AllowAnonymous]
    public IActionResult Login(string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;
        return View(new LoginVm());
    }

    [HttpPost, AllowAnonymous, EnableRateLimiting("login")]
    public async Task<IActionResult> Login(LoginVm vm, string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;
        if (!ModelState.IsValid)
        {
            return View(vm);
        }

        var user = await _accounts.ValidateCredentialsAsync(vm.Login, vm.Password);
        if (user is null)
        {
            // Однакове повідомлення для неіснуючого логіна та хибного пароля.
            ModelState.AddModelError(string.Empty, "Невірний логін або пароль.");
            vm.Password = string.Empty;
            return View(vm);
        }

        await SignInAsync(user);
        return !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl)
            ? LocalRedirect(returnUrl)
            : RedirectToAction("Index", "Requests");
    }

    [HttpGet, AllowAnonymous]
    public IActionResult Register() => View(new RegisterVm());

    [HttpPost, AllowAnonymous]
    public async Task<IActionResult> Register(RegisterVm vm)
    {
        if (!ModelState.IsValid)
        {
            vm.Password = string.Empty;
            return View(vm);
        }

        var result = await _accounts.RegisterClientAsync(vm.Login, vm.DisplayName, vm.Password);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.Message ?? "Помилка реєстрації.");
            vm.Password = string.Empty;
            return View(vm);
        }

        TempData["Success"] = "Реєстрацію завершено. Увійдіть у систему.";
        return RedirectToAction(nameof(Login));
    }

    [HttpPost, Authorize]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction(nameof(Login));
    }

    [HttpGet, AllowAnonymous]
    public IActionResult Denied() => View();

    private Task SignInAsync(AppUser user)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.DisplayName),
            new(ClaimTypes.Role, user.Role.ToString())
        };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        return HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity));
    }
}

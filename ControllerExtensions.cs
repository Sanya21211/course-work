using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using ServiceCenter.Web.Domain;
using ServiceCenter.Web.Services;

namespace ServiceCenter.Web.Controllers;

public static class ControllerExtensions
{
    /// <summary>Особу й роль беремо лише з перевіреного cookie, а не з даних форми.</summary>
    public static Actor GetActor(this ControllerBase controller)
    {
        var id = int.Parse(controller.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var role = Enum.Parse<UserRole>(controller.User.FindFirstValue(ClaimTypes.Role)!);
        return new Actor(id, role);
    }
}

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Stelinda.Api.Filters;

/// <summary>
/// İstek yalnız oturumdaki kullanıcı adına olabilir: yoldaki <c>id</c> ya da
/// <c>userId</c> ve gövdedeki <c>UserId</c>, oturum anahtarındaki kullanıcıyla
/// aynı değilse 403. Önceden uçların çoğu isteğe yazılan kimlik adına
/// çalışıyordu: başkasının puanı, bileti, serisi ve fotoğrafı
/// değiştirilebiliyordu (güvenlik taraması, 23 Kas 2026).
///
/// Kimliksiz uçlara ([AllowAnonymous]: giriş, Apple bildirimi, kart yönetimi)
/// bakılmaz. Başka bir kullanıcıyı gösteren alanlar (<c>BlockedUserId</c>,
/// <c>ReportedUserId</c>) bu yüzden <c>UserId</c> adını taşımaz.
/// </summary>
public sealed class OwnUserFilter : IActionFilter
{
    private static readonly string[] RouteKeys = { "id", "userId" };

    public void OnActionExecuting(ActionExecutingContext context)
    {
        if (context.ActionDescriptor.EndpointMetadata.OfType<IAllowAnonymous>().Any()) return;

        // Kimlik doğrulaması genel kuralla (FallbackPolicy) zorunlu; burada
        // kimlik yoksa istek zaten 401 almıştır.
        var me = context.HttpContext.User.Identity?.Name;
        if (string.IsNullOrEmpty(me)) return;

        foreach (var key in RouteKeys)
        {
            if (context.ActionArguments.TryGetValue(key, out var value) &&
                value is string routeId &&
                !string.Equals(routeId, me, StringComparison.Ordinal))
            {
                context.Result = new ForbidResult();
                return;
            }
        }

        foreach (var argument in context.ActionArguments.Values)
        {
            if (argument is null or string) continue;
            var property = argument.GetType().GetProperty("UserId");
            if (property?.PropertyType != typeof(string)) continue;
            if (property.GetValue(argument) is string bodyId &&
                !string.Equals(bodyId, me, StringComparison.Ordinal))
            {
                context.Result = new ForbidResult();
                return;
            }
        }
    }

    public void OnActionExecuted(ActionExecutedContext context)
    {
    }
}

using Microsoft.Extensions.Options;
using OpenOsuTournament.Bancho.Application.Configuration;

namespace OpenOsuTournament.Bancho.Web.Routing;

/// <summary>
///     Gates every route in <see cref="AdminManagementRoutes" /> behind the X-Admin-Key header. An
///     unset <see cref="AdminApiOptions.AdminKey" /> locks every management route down (401) rather
///     than leaving them open — there is no "management API is just unauthenticated" mode.
/// </summary>
internal sealed class AdminKeyFilter(IOptions<AdminApiOptions> options) : IEndpointFilter
{
    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var adminKey = options.Value.AdminKey;
        var provided = context.HttpContext.Request.Headers["X-Admin-Key"].FirstOrDefault();

        if (string.IsNullOrEmpty(adminKey) || provided != adminKey)
            return ValueTask.FromResult<object?>(Results.Unauthorized());

        return next(context);
    }
}

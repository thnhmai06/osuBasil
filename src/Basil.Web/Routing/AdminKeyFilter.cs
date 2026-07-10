using Basil.Application.Configuration;
using Microsoft.Extensions.Options;

namespace Basil.Web.Routing;

/// <summary>
///     Gates every route in <see cref="AdminManagementRoutes" /> behind the X-Admin-Key header. An
///     unset <see cref="ServerOptions.AdminKey" /> locks every management route down (401) rather
///     than leaving them open — there is no "management API is just unauthenticated" mode.
/// </summary>
internal sealed class AdminKeyFilter(IOptions<ServerOptions> options) : IEndpointFilter
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
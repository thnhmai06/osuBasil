using System.Security.Claims;
using System.Text.Encodings.Web;
using Basil.Application.Configuration;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Basil.Web.Auth;

/// <summary>
///     Reads the <c>X-Admin-Key</c> header and, if it matches the configured
///     <see cref="ServerOptions.AdminKey" />, authenticates the request with a <see cref="ClaimsPrincipal" />
///     carrying the <see cref="AdminKeyDefaults.Role" /> role. A missing, empty, or wrong key returns
///     <see cref="AuthenticateResult.NoResult" /> rather than <see cref="AuthenticateResult.Fail(string)" /> —
///     the request is simply left anonymous instead of being rejected outright, so routes with no
///     <c>[Authorize]</c> requirement are unaffected and can still check <c>User.IsInRole(AdminKeyDefaults.Role)</c>
///     to decide whether to reveal an otherwise-hidden resource (private matches, frozen beatmaps/mapsets).
///     Mutation routes instead require the role via <c>RequireAuthorization(AdminKeyDefaults.Policy)</c>, letting
///     the framework's own authorization middleware 401 automatically when the role is missing.
/// </summary>
public sealed class AdminKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IOptions<ServerOptions> serverOptions)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var adminKey = serverOptions.Value.AdminKey;
        var provided = Request.Headers["X-Admin-Key"].FirstOrDefault();

        if (string.IsNullOrEmpty(adminKey) || string.IsNullOrEmpty(provided) || provided != adminKey)
            return Task.FromResult(AuthenticateResult.NoResult());

        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Role, AdminKeyDefaults.Role)], Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}

/// <summary>Scheme/policy/role names for <see cref="AdminKeyAuthenticationHandler" />, shared by <c>Program.cs</c>.</summary>
public static class AdminKeyDefaults
{
    public const string Scheme = "AdminKey";
    public const string Policy = "Admin";
    public const string Role = "Admin";
}

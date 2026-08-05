using System.Security.Claims;
using System.Text.Encodings.Web;
using Basil.Application.Services.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Basil.Web.Auth;

/// <summary>
///     Reads the <c>Authorization: Bearer &lt;key&gt;</c> header and authenticates the request with a
///     <see cref="ClaimsPrincipal" /> carrying the <see cref="AdminKeyDefaults.Role" /> role, either
///     because the key matches the hash <see cref="AdminKeyService" /> has stored, or because the
///     server has no hash configured at all (bypass mode). A missing, empty, or wrong key returns
///     <see cref="AuthenticateResult.NoResult" /> rather than <see cref="AuthenticateResult.Fail(string)" />.
///     The request is left anonymous instead of being rejected outright, so routes with no
///     <c>[Authorize]</c> requirement are unaffected and can still check
///     <c>User.IsInRole(AdminKeyDefaults.Role)</c> to decide whether to reveal an otherwise-hidden
///     resource (private matches, frozen beatmaps, or beatmapsets). Mutation routes instead require
///     the role via <c>RequireAuthorization(AdminKeyDefaults.Policy)</c>, so the framework's own
///     authorization middleware 401s automatically when the role is missing.
/// </summary>
public sealed class AdminKeyAuthenticationHandler(
	IOptionsMonitor<AuthenticationSchemeOptions> options,
	ILoggerFactory logger,
	UrlEncoder encoder,
	AdminKeyService adminKeyService)
	: AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
	private const string BearerPrefix = "Bearer ";

	/// <summary>Authenticates the current request against the stored admin key hash, or bypass mode.</summary>
	/// <returns>
	///     An authenticated ticket with the admin role when the key matches or bypass mode is active,
	///     or <see cref="AuthenticateResult.NoResult" /> when the key is missing or wrong.
	/// </returns>
	protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
	{
		if (await adminKeyService.IsBypassAsync(Context.RequestAborted)) return Success();

		var authorization = Request.Headers.Authorization.FirstOrDefault();
		var provided = authorization is not null && authorization.StartsWith(BearerPrefix, StringComparison.Ordinal)
			? authorization[BearerPrefix.Length..]
			: null;

		if (string.IsNullOrEmpty(provided) || !await adminKeyService.VerifyAsync(provided, Context.RequestAborted))
		{
			// Only log when a key was actually attempted. Most requests to the api host carry no
			// Authorization header at all (the soft private/frozen-visibility check runs on every
			// request), and logging that as a "failure" would be pure noise.
			if (!string.IsNullOrEmpty(provided)) Logger.LogInformation("Admin auth failed: Path={Path}", Request.Path);
			return AuthenticateResult.NoResult();
		}

		Logger.LogInformation("Admin auth succeeded: Path={Path}", Request.Path);
		return Success();
	}

	/// <summary>Builds a succeeded authentication result carrying the admin role.</summary>
	private AuthenticateResult Success()
	{
		var identity = new ClaimsIdentity([new Claim(ClaimTypes.Role, AdminKeyDefaults.Role)], Scheme.Name);
		var principal = new ClaimsPrincipal(identity);
		var ticket = new AuthenticationTicket(principal, Scheme.Name);
		return AuthenticateResult.Success(ticket);
	}
}

/// <summary>Scheme/policy/role names for <see cref="AdminKeyAuthenticationHandler" />, shared by <c>Program.cs</c>.</summary>
public static class AdminKeyDefaults
{
	/// <summary>The authentication scheme name used by <see cref="AdminKeyAuthenticationHandler" />.</summary>
	public const string Scheme = "AdminKey";

	/// <summary>The authorization policy name that requires the <see cref="Role" /> role.</summary>
	public const string Policy = "Admin";

	/// <summary>The role claimed on an authenticated admin principal and required by the <see cref="Policy" /> policy.</summary>
	public const string Role = "Admin";
}
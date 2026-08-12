using System.Security.Claims;
using System.Text.Encodings.Web;
using Basil.Application.Services.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Basil.Web.Auth;

/// <summary>
///     Authenticates requests using the <c>Authorization: Bearer &lt;key&gt;</c> header,
///     granting the <see cref="AdminKeyDefaults.Role" /> role when the supplied key is
///     valid or the server is running in bypass mode.
/// </summary>
/// <remarks>
///     A request that carries no <c>Authorization</c> header returns
///     <see cref="AuthenticateResult.NoResult" />, leaving it anonymous rather
///     than rejecting it outright. A request that carries one but the key is
///     malformed or wrong returns <see cref="AuthenticateResult.Fail(string)" />
///     instead, since an authentication attempt was actually made.
///     Endpoints can optionally check
///     <c>User.IsInRole(AdminKeyDefaults.Role)</c> to reveal privileged
///     resources, while administrative endpoints should require
///     <c>RequireAuthorization(AdminKeyDefaults.Policy)</c>. The authorization
///     middleware will then return <c>401 Unauthorized</c> automatically when
///     the role is absent.
/// </remarks>
public sealed class AdminKeyAuthenticationHandler(
	IOptionsMonitor<AuthenticationSchemeOptions> options,
	ILoggerFactory logger,
	UrlEncoder encoder,
	AdminKeyService adminKeyService)
	: AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
	private const string BearerPrefix = "Bearer ";

	/// <summary>Authenticates the current request against the stored admin key hash or bypass mode.</summary>
	/// <returns>
	///     An authenticated ticket with the admin role when the key matches or bypass mode is active,
	///     <see cref="AuthenticateResult.NoResult" /> when no key was supplied, or
	///     <see cref="AuthenticateResult.Fail(string)" /> when a supplied key is malformed or wrong.
	/// </returns>
	protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
	{
		if (await adminKeyService.IsBypassAsync(Context.RequestAborted)) return Success();

		var authorization = Request.Headers.Authorization.FirstOrDefault();
		if (authorization is null) return AuthenticateResult.NoResult();

		var provided = authorization.StartsWith(BearerPrefix, StringComparison.Ordinal)
			? authorization[BearerPrefix.Length..]
			: null;

		if (string.IsNullOrEmpty(provided) || !await adminKeyService.VerifyAsync(provided, Context.RequestAborted))
		{
			Logger.LogInformation("Admin auth failed: Path={Path}", Request.Path);
			return AuthenticateResult.Fail("Invalid admin key");
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
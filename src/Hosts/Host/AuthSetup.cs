using Basil.Host.Api.Auth;
using Microsoft.AspNetCore.Authentication;

namespace Basil.Host;

/// <summary>Registers admin-key authentication and authorization for the host.</summary>
internal static class AuthSetup
{
	/// <summary>
	///     Registers the admin-key authentication scheme and the admin-role authorization policy.
	/// </summary>
	/// <remarks>
	///     The scheme reads the <c>Authorization: Bearer</c> header (see
	///     <see cref="AdminKeyAuthenticationHandler" />). One mechanism serves both the hard
	///     admin-only gate (<c>RequireAuthorization</c>) and the soft private/frozen-visibility
	///     elevation (<c>User.IsInRole</c>).
	/// </remarks>
	/// <param name="builder">The web application builder whose authentication services are registered.</param>
	public static void Configure(WebApplicationBuilder builder)
	{
		builder.Services
			.AddAuthentication(AdminKeyDefaults.Scheme)
			.AddScheme<AuthenticationSchemeOptions, AdminKeyAuthenticationHandler>(AdminKeyDefaults.Scheme, null);

		builder.Services.AddAuthorizationBuilder()
			.AddPolicy(AdminKeyDefaults.Policy,
				policy => policy.RequireRole(AdminKeyDefaults.Role));
	}
}
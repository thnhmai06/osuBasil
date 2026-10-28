using Basil.Application.Auth.Persistence;
using Basil.Application.Content.Persistence;
using Basil.Application.Users.Connections;
using Basil.Application.Users.Users;
using Basil.Domain.Auth;
using Basil.Domain.Common;
using Basil.Domain.Users;
using Basil.Host.Bancho.Support;

namespace Basil.Host.Bancho.Web;

/// <summary>Authenticates the web requests of an osu! client by its username and password hash.</summary>
internal sealed class WebAuthentication(
	UserRegistry registry,
	ICredentialRepository credentials,
	ISettingsRepository settings,
	RateLimiter limiter,
	TimeProvider clock)
{
	/// <summary>Tells whether a client may use osu!direct.</summary>
	/// <param name="client">The authenticated client.</param>
	/// <returns><see langword="true" /> when its effective permissions include osu!direct.</returns>
	public bool MayUseDirect(BanchoConnection client)
	{
		return client.User.EffectivePermissions(clock.GetUtcNow()).Allows(UserPermissions.BeatmapsetDirect);
	}

	/// <summary>Finds the online osu! client a web request speaks for.</summary>
	/// <param name="context">The request, whose caller address the failed attempts are counted against.</param>
	/// <param name="username">The username the client sent.</param>
	/// <param name="passwordMd5">The MD5 digest of the password the client sent.</param>
	/// <param name="cancellationToken">A token that cancels the check.</param>
	/// <returns>The client's connection, or <see langword="null" /> when nobody online has that name and password.</returns>
	public async Task<BanchoConnection?> AuthenticateAsync(HttpContext context, string? username, string? passwordMd5,
		CancellationToken cancellationToken)
	{
		if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(passwordMd5)) return null;
		var normalized = Basil.Domain.Users.UserData.NormalizeName(username);
		var online = registry.Online.Values.FirstOrDefault(user => user.Record.Value.NormalizedName == normalized);
		if (online?.Bancho is not { IsOpen: true } client) return null;
		var rules = (await settings.GetAsync(cancellationToken)).RateLimits.Login;
		var ipKey = $"login-fail:{ClientAddress.Resolve(context)}";
		var pairKey = $"{ipKey}:{normalized}";
		if (limiter.Count(ipKey, rules.Window) >= rules.MaxAttempts * 4 ||
		    limiter.Count(pairKey, rules.Window) >= rules.MaxAttempts)
			return null;

		try
		{
			if (await credentials.VerifyAsync(new Credentials(online.Record, new Md5(passwordMd5)), cancellationToken))
				return client;
		}
		catch (ArgumentException)
		{
		}

		limiter.Record(ipKey);
		limiter.Record(pairKey);
		return null;
	}
}
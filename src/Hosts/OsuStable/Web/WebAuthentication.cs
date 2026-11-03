using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Basil.Application.Auth.Persistence;
using Basil.Application.Content.Persistence;
using Basil.Application.Users.Objects.Connections;
using Basil.Application.Users.Persistence;
using Basil.Application.Users.Registry;
using Basil.Domain.Auth;
using Basil.Domain.Common;
using Basil.Domain.Users;
using Basil.Host.OsuStable.Support;

namespace Basil.Host.OsuStable.Web;

/// <summary>Authenticates the web requests of an osu! client by its username and password hash.</summary>
internal sealed class WebAuthentication(
	UserRegistry registry,
	IUserRepository users,
	ICredentialRepository credentials,
	ISettingsRepository settings,
	LoginAttemptGuard attempts,
	TimeProvider clock)
{
	// The digest that passed the password check, per connection: later requests skip the slow hash check.
	private readonly ConditionalWeakTable<OsuStableConnection, string> _verified = new();

	/// <summary>Tells whether a client may use osu!direct.</summary>
	/// <param name="client">The authenticated client.</param>
	/// <returns><see langword="true" /> when its effective permissions include osu!direct.</returns>
	public bool MayUseDirect(OsuStableConnection client)
	{
		return Effective(client).Allows(UserPermissions.BeatmapsetDirect);
	}

	/// <summary>Gets what a client may do right now.</summary>
	/// <param name="client">The authenticated client.</param>
	/// <returns>Its effective permissions.</returns>
	public UserPermissions Effective(OsuStableConnection client)
	{
		return client.User.EffectivePermissions(clock.GetUtcNow());
	}

	/// <summary>Finds the online osu! client a web request speaks for.</summary>
	/// <param name="context">The request, whose caller address the failed attempts are counted against.</param>
	/// <param name="username">The username the client sent.</param>
	/// <param name="passwordMd5">The MD5 digest of the password the client sent.</param>
	/// <param name="cancellationToken">A token that cancels the check.</param>
	/// <returns>The client's connection, or <see langword="null" /> when nobody online has that name and password.</returns>
	public async Task<OsuStableConnection?> AuthenticateAsync(HttpContext context, string? username, string? passwordMd5,
		CancellationToken cancellationToken)
	{
		if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(passwordMd5)) return null;
		var normalized = UserData.NormalizeName(username);
		if (await users.GetByNameAsync(username, cancellationToken) is not { } record) return null;
		var online = registry.Of(record);
		if (online.OsuStable is not { IsOpen: true } client) return null;
		var rules = (await settings.GetAsync(cancellationToken)).RateLimits.Login;
		var ipKey = $"login-fail:{ClientAddress.Resolve(context)}";
		var pairKey = $"{ipKey}:{normalized}";
		using var attempt = attempts.TryReserve(ipKey, pairKey, rules.MaxAttempts, rules.Window);
		if (attempt is null) return null;

		try
		{
			if (_verified.TryGetValue(client, out var known) && SameDigest(known, passwordMd5)) return client;
			if (await credentials.VerifyAsync(new Credentials(online.Record, new Md5(passwordMd5)), cancellationToken))
			{
				_verified.AddOrUpdate(client, passwordMd5.ToLowerInvariant());
				return client;
			}
		}
		catch (ArgumentException)
		{
		}

		attempt.Keep();
		return null;
	}

	private static bool SameDigest(string known, string given)
	{
		return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(known),
			Encoding.UTF8.GetBytes(given.ToLowerInvariant()));
	}
}
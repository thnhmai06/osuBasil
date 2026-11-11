using Basil.Application.Shared.Settings;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Basil.Application.Users.Objects.Connections;
using Basil.Application.Users.Messages;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Registry;
using Basil.Domain.Shared;
using Basil.Domain.Cryptography;
using Basil.Domain.Users;
using Basil.Gateway;
using Basil.Gateway.RateLimiting;
using Mediator;
using Basil.Host.Stable.Support;

namespace Basil.Host.Stable.Web;

/// <summary>Authenticates the web requests of an osu! client by its username and password hash.</summary>
internal sealed class WebAuthentication(
	UserRegistry registry,
	ISender sender,
	LoginAttemptGuard attempts,
	TimeProvider clock)
{
	// The digest that passed the password check, per connection: later requests skip the slow hash check.
	private readonly ConditionalWeakTable<StableConnection, string> _verified = new();

	/// <summary>Tells whether a client may use osu!direct.</summary>
	/// <param name="client">The authenticated client.</param>
	/// <returns><see langword="true" /> when its effective privileges include osu!direct.</returns>
	public bool MayUseDirect(StableConnection client)
	{
		return Effective(client).Allows(UserPrivileges.Supporter);
	}

	/// <summary>Gets what a client may do right now.</summary>
	/// <param name="client">The authenticated client.</param>
	/// <returns>Its effective privileges.</returns>
	public UserPrivileges Effective(StableConnection client)
	{
		return client.User.EffectivePrivileges(clock.GetUtcNow());
	}

	/// <summary>Finds the online osu! client a web request speaks for.</summary>
	/// <param name="context">The request, whose caller address the failed attempts are counted against.</param>
	/// <param name="username">The username the client sent.</param>
	/// <param name="passwordMd5">The MD5 digest of the password the client sent.</param>
	/// <param name="ct">A token that cancels the check.</param>
	/// <returns>The client's connection, or <see langword="null" /> when nobody online has that name and password.</returns>
	public async Task<StableConnection?> AuthenticateAsync(HttpContext context, string? username,
		string? passwordMd5,
		CancellationToken ct)
	{
		if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(passwordMd5)) return null;
		var normalized = UserData.NormalizeName(username);
		if (await sender.Send(new GetUser(UserReference.ByName(username)), ct) is not { } record) return null;
		var online = registry.Of(record);
		if (online.Stable is not { IsOpen: true } client) return null;
		var rules = (await sender.Send(new ReadSettings(), ct)).RateLimits.Login;
		var ipKey = $"login-fail:{ClientAddress.Resolve(context)}";
		var pairKey = $"{ipKey}:{normalized}";
		using var attempt = attempts.TryReserve(ipKey, pairKey, rules.MaxAttempts, rules.Window);
		if (attempt is null) return null;

		if (_verified.TryGetValue(client, out var known) && SameDigest(known, passwordMd5)) return client;
		if (Md5.TryParse(passwordMd5, null, out var digest) &&
		    await sender.Send(new VerifyUserPassword(online, digest), ct))
		{
			_verified.AddOrUpdate(client, passwordMd5.ToLowerInvariant());
			return client;
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
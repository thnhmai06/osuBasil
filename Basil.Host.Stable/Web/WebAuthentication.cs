using Basil.Application.Users;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Basil.Application.Shared.Settings;
using Basil.Application.Users.Messages;
using Basil.Application.Users.Objects.Sessions;
using Basil.Application.Users.Registry;
using Basil.Domain.Users;
using Basil.Gateway.RateLimiting;
using Basil.Host.Stable.Support;
using Basil.Shared.Structures.Cryptography;
using Mediator;

namespace Basil.Host.Stable.Web;

internal sealed class WebAuthentication(
	UserRegistry registry,
	ISender sender,
	LoginAttemptGuard attempts,
	TimeProvider clock)
{
	// The digest that passed the password check, per connection: later requests skip the slow hash check.
	private readonly ConditionalWeakTable<StableSession, string> _verified = new();

	public bool MayUseDirect(StableSession client)
	{
		return Effective(client).Allows(UserPrivileges.Supporter);
	}

	public UserPrivileges Effective(StableSession client)
	{
		return client.User.EffectivePrivileges(clock.GetUtcNow());
	}

	public async Task<StableSession?> AuthenticateAsync(HttpContext context, string? username,
		string? passwordMd5,
		CancellationToken ct)
	{
		if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(passwordMd5)) return null;
		var normalized = UserData.TryNormalize(username);
		if (normalized is null) return null;
		if (await sender.Send(new FindUserByName(username), ct) is not { } record) return null;
		var online = registry.Of(record);
		if (online.Stable is not { IsOpen: true } client) return null;
		var rules = (await sender.Send(new ReadSettings(), ct)).RateLimits.Login;
		var ipKey = $"login-fail:{ClientAddress.Resolve(context)}";
		var pairKey = $"{ipKey}:{normalized}";
		using var attempt = attempts.TryReserve(ipKey, pairKey, rules.MaxAttempts, rules.Window);
		if (attempt is null) return null;

		if (_verified.TryGetValue(client, out var known) && SameDigest(known, passwordMd5)) return client;
		if (Md5Hash.TryParse(passwordMd5, null, out var digest) &&
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
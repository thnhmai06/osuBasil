using Basil.Host.Shared.Http;
using Basil.Application.Sessions.Messages;
using Basil.Application.Sessions.Registry;
using Basil.Application.Users;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Basil.Application.Shared.Settings;
using Basil.Application.Users.Messages;
using Basil.Application.Sessions.Objects;
using Basil.Domain.Users;
using Basil.Gateway.RateLimiting;
using Basil.Host.Stable.Support;
using Basil.Shared.Structures.Cryptography;
using Mediator;

namespace Basil.Host.Stable.Web;

internal sealed class WebAuthentication(
	SessionRegistry registry,
	ISender sender,
	LoginAttemptGuard attempts)
{
	// The digest that passed the password check, per connection: later requests skip the slow hash check.
	private readonly ConditionalWeakTable<StableSession, string> _verified = new();

	/// <summary>Tells whether the request comes from an address that has an open osu! session.</summary>
	public bool HasOpenClientAt(HttpContext context)
	{
		return OpenClientAt(context) is not null;
	}

	/// <summary>The open osu! session at the address the request comes from, or null when there is none.</summary>
	public StableSession? OpenClientAt(HttpContext context)
	{
		var address = ClientAddress.Resolve(context);
		// shortcut: scans the online users, like AuthenticateAsync; add an index by address if the online count grows large.
		return registry.OpenStableClients()
			.FirstOrDefault(client => client.State.Record.Value.Fingerprint.Ip.Equals(address));
	}

	public async Task<StableSession?> AuthenticateAsync(HttpContext context, string? username,
		string? passwordMd5,
		CancellationToken ct)
	{
		if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(passwordMd5)) return null;
		var normalized = UserData.TryNormalize(username);
		if (normalized is null) return null;
		// shortcut: scans the online users; the client must be online anyway, add an index if the online count grows large.
		var online = registry.Online.Values.FirstOrDefault(user => user.Record.Value.NormalizedName == normalized);
		if (online is null) return null;
		if (online.Stable is not { IsOpen: true } client) return null;
		var rules = (await sender.Send(new ReadSettings(), ct)).RateLimits.Login;
		var ipKey = $"login-fail:{ClientAddress.Resolve(context)}";
		var pairKey = $"{ipKey}:{normalized}";
		using var attempt = attempts.TryReserve(ipKey, pairKey, rules.MaxAttempts, rules.Window);
		if (attempt is null) return null;

		if (_verified.TryGetValue(client, out var known) && SameDigest(known, passwordMd5)) return client;
		if (Md5Hash.TryParse(passwordMd5, null, out var digest) &&
		    await sender.Send(new VerifyUserPassword(online.User, digest), ct))
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
using Basil.Host.Shared.Http;
using Basil.Application.Sessions.Messages;
using Basil.Application.Sessions.Registry;
using Basil.Application.Sessions.Objects;
using Basil.Application.Sessions.Persistence;
using Basil.Gateway.RateLimiting;
using Basil.Host.Stable.Support;
using Mediator;

namespace Basil.Host.Stable.Web;

internal sealed class WebAuthentication(
	SessionRegistry registry,
	ISender sender,
	SignInGate signIns)
{
	/// <summary>The open osu! session at the address the request comes from, or null when there is none.</summary>
	public StableSession? OpenClientAt(HttpContext context)
	{
		var address = ClientAddress.Resolve(context);
		// shortcut: scans the online users; add an index by address if the online count grows large.
		return registry.OpenStableClients()
			.FirstOrDefault(client => client.State.Record.Value.Fingerprint.Ip.Equals(address));
	}

	public async Task<StableSession?> AuthenticateAsync(HttpContext context, string? username,
		string? passwordMd5,
		CancellationToken ct)
	{
		if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(passwordMd5)) return null;
		using var attempt = signIns.Admit(ClientAddress.Resolve(context), username).Attempt;
		if (attempt is null) return null;

		var result = await sender.Send(new SignInWeb(username, passwordMd5), ct);
		if (result.Session is StableSession client) return client;

		if (result.Failure is LoginFailure.WrongPassword) attempt.Fail();
		return null;
	}
}
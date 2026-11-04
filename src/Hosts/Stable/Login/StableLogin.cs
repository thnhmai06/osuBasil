using Basil.Application.Users.Messages;
using Mediator;
using System.Net;
using Basil.Application.Chat.Objects;
using Basil.Application.Chat.Registry;
using Basil.Application.Content.Persistence;
using Basil.Application.Scores.Persistence;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Objects.Connections;
using Basil.Application.Users.Persistence;
using Basil.Application.Users.Registry;
using Basil.Domain.Authentication;
using Basil.Domain.Client;
using Basil.Domain.Common;
using Basil.Domain.Mechanics;
using Basil.Domain.Users;
using Basil.Gateway;
using Basil.Host.Stable.Support;
using Basil.Protocol.Stable.Models.Authentication;

namespace Basil.Host.Stable.Login;

/// <summary>Logs osu! clients in and builds the first reply of a session.</summary>
internal sealed class StableLogin(
	UserRegistry registry,
	GeneralChannelRegistry channels,
	IUserStatsRepository statsRepository,
	ISettingsRepository settings,
	Outbox outbox,
	LoginAttemptGuard attempts,
	StableState state,
	TimeProvider clock,
	ISender sender)
{
	private const int ProtocolVersion = 19;

	/// <summary>Handles a request that carries no token: a login.</summary>
	/// <param name="context">The request.</param>
	/// <param name="body">The login body.</param>
	/// <param name="cancellationToken">A token that cancels the login.</param>
	/// <returns>The token for the client (or a placeholder) and the packets to answer with.</returns>
	public async Task<(string Token, byte[] Reply)> LoginAsync(HttpContext context, byte[] body,
		CancellationToken cancellationToken)
	{
		var rules = (await settings.GetAsync(cancellationToken)).RateLimits;
		var ip = ClientAddress.Resolve(context);
		if (!state.AcceptingLogins) return Fail(LoginFailureReason.ErrorOccurred, StableReplies.ServerStopping);

		if (!TryParse(body, out var request, out var client))
			return Fail(LoginFailureReason.ErrorOccurred);

		var ipKey = $"login-fail:{ip}";
		var pairKey = $"{ipKey}:{request.Username.ToLowerInvariant()}";
		// The attempt is reserved before the check and stays counted only for wrong credentials.
		using var attempt = attempts.TryReserve(ipKey, pairKey, rules.Login.MaxAttempts, rules.Login.Window);
		if (attempt is null) return Fail(LoginFailureReason.AuthenticationFailed, StableReplies.TooManyAttempts);

		if (CountOpenConnections(ip) >= rules.Connections.PerIp)
			return Fail(LoginFailureReason.ErrorOccurred, StableReplies.TooManyConnections);

		var type = client.Version.Stream == OsuVersionStream.Tourney ? SessionType.StableTourney : SessionType.Stable;
		LoginResult result;
		try
		{
			result = await registry.LoginAsync(new LoginAttempt(request.Username, new Md5(request.PasswordMd5)), type,
				ip,
				client, request.UtcOffset, cancellationToken);
		}
		catch (ArgumentException)
		{
			return Fail(LoginFailureReason.AuthenticationFailed);
		}

		if (result.Connection is not { } connection)
		{
			if (result.Failure is LoginFailure.UnknownUser or LoginFailure.WrongPassword) attempt.Keep();
			return FailureReply(result.Failure);
		}

		if (request.BlockNonFriendDms) await sender.Send(new SetPmPrivate(connection, true), cancellationToken);
		return (connection.Token, await WelcomeAsync(connection, cancellationToken));
	}

	private static bool TryParse(byte[] body, out LoginRequest request, out OsuClient client)
	{
		try
		{
			request = LoginRequest.Parse(body);
			client = new OsuClient(request.OsuVersion, request.Fingerprint);
			return true;
		}
		catch (Exception ex) when (ex is FormatException or ArgumentException)
		{
			request = null!;
			client = null!;
			return false;
		}
	}

	private int CountOpenConnections(IPAddress? ip)
	{
		return registry.Online.Values.SelectMany(user => user.Connections)
			.Count(connection => connection.IsOpen && Equals(connection.Session.Value.Device.Ip, ip));
	}

	private static (string Token, byte[] Reply) FailureReply(LoginFailure? failure)
	{
		return failure switch
		{
			LoginFailure.TooManySessions => Fail(LoginFailureReason.AuthenticationFailed,
				StableReplies.TooManySessions),
			LoginFailure.NoTourneyPermission => Fail(LoginFailureReason.AuthenticationFailed,
				StableReplies.NoTourneyPermission),
			_ => Fail(LoginFailureReason.AuthenticationFailed)
		};
	}

	private async Task<byte[]> WelcomeAsync(Connection connection, CancellationToken cancellationToken)
	{
		var user = connection.User;
		var now = clock.GetUtcNow();
		var effective = user.EffectivePermissions(now);
		var roles = StableView.Roles(effective);
		var privileges = (int)roles;
		var motd = (await settings.GetAsync(cancellationToken)).General.Motd;
		var friends = (await sender.Send(new ListFriends(connection, user.Record), cancellationToken)).Users;
		var restricted = (user.Record.Value.Permissions & ~effective) != UserPermissions.None;
		var others = new List<(User User, UserStats Stats)>();
		foreach (var other in registry.Online.Values.Where(other => !ReferenceEquals(other, user)))
			others.Add((other, await statsRepository.GetAsync(other.Record,
				other.Stable?.Status.GameMode ?? GameMode.Standard, cancellationToken)));

		var welcome = Outbox.Build(writer =>
		{
			writer.ProtocolVersion(ProtocolVersion);
			writer.LoginReply(user.Record.Id);
			writer.Privileges(privileges);
			if (restricted) writer.AccountRestricted();
			if (!string.IsNullOrEmpty(motd)) writer.Notification(motd);
			writer.FriendsList([.. friends.Select(friend => friend.Id)]);
			foreach (var channel in channels.All.Values.Where(channel => ReadableBy(channel, roles)))
				writer.ChannelInfo(channel.Name, channel.Topic, channel.Members.Count);
			writer.ChannelInfoEnd();
			foreach (var (other, stats) in others)
			{
				StableView.WritePresence(writer, other, stats, now);
				StableView.WriteStats(writer, other, stats);
			}
		});
		return [.. welcome, .. outbox.Drain(connection)];
	}

	private static bool ReadableBy(GeneralChannel channel, UserPrivileges roles)
	{
		var read = channel.Record.Value.ReadPrivileges;
		return channel.Record.Value.Visible && roles.Allows(read);
	}

	private static (string Token, byte[] Reply) Fail(LoginFailureReason reason, string? notification = null)
	{
		return ("no", Outbox.Build(writer =>
		{
			if (notification is not null) writer.Notification(notification);
			writer.LoginReply((int)reason);
		}));
	}
}
using Basil.Application.Sessions.Messages;
using Basil.Host.Shared.Http;
using Basil.Application.Sessions.Objects;
using Basil.Application.Sessions.Persistence;
using Basil.Application.Sessions.Registry;
using Basil.Application.Users;
using System.Net;
using Basil.Application.Chat.Registry;
using Basil.Application.Scores.Messages;
using Basil.Application.Shared.Settings;
using Basil.Application.Users.Messages;
using Basil.Application.Users.Objects;
using Basil.Domain.Sessions;
using Basil.Domain.Client;
using Basil.Domain.Game;
using Basil.Domain.Users;
using Basil.Gateway.RateLimiting;
using Basil.Host.Stable.Support;
using Basil.Protocol.Game.Stable.Models.Authentication;
using Basil.Shared.Structures.Cryptography;
using Mediator;
using Session = Basil.Application.Sessions.Objects.Session;
using UserSessions = Basil.Application.Sessions.Objects.UserSessions;

namespace Basil.Host.Stable.Login;

internal sealed class StableLogin(
	GeneralChannelRegistry channels,
	Outbox outbox,
	SignInGate signIns,
	StableState state,
	TimeProvider clock,
	ISender sender)
{
	private const int ProtocolVersion = 19;

	public async Task<(string Token, byte[] Reply)> LoginAsync(HttpContext context, byte[] body,
		CancellationToken ct)
	{
		var ip = ClientAddress.Resolve(context);
		if (!state.AcceptingLogins) return Fail(LoginFailureReason.ErrorOccurred, StableReplies.ServerStopping);

		if (!TryParse(body, out var request, out var client))
			return Fail(LoginFailureReason.ErrorOccurred);

		// The attempt is reserved before the check and stays counted only for wrong credentials.
		using var attempt = signIns.Admit(ip, request.Username).Attempt;
		if (attempt is null) return Fail(LoginFailureReason.AuthenticationFailed, StableReplies.TooManyAttempts);

		var type = client.Version.Stream == OsuVersionStream.Tourney ? SessionType.StableTourney : SessionType.Stable;
		LoginResult result;
		try
		{
			result = await sender.Send(new LoginSession(new LoginAttempt(request.Username, new Md5Hash(request.PasswordMd5)),
				type,
				ip,
				client, request.UtcOffset), ct);
		}
		catch (ArgumentException)
		{
			return Fail(LoginFailureReason.AuthenticationFailed);
		}

		if (result.Session is not { } connection)
		{
			attempt.End(result);
			return FailureReply(result.Failure);
		}

		if (request.BlockNonFriendDms) await sender.Send(new SetPmPrivate(connection, true), ct);
		return (connection.Token, await WelcomeAsync(connection, ct));
	}

	private static bool TryParse(byte[] body, out LoginRequest request, out ClientBuild client)
	{
		try
		{
			request = LoginRequest.Parse(body);
			client = new ClientBuild(request.OsuVersion, request.Fingerprint);
			return true;
		}
		catch (Exception ex) when (ex is FormatException or ArgumentException)
		{
			request = null!;
			client = null!;
			return false;
		}
	}

	private static (string Token, byte[] Reply) FailureReply(LoginFailure? failure)
	{
		return failure switch
		{
			LoginFailure.AlreadyOnline => Fail(LoginFailureReason.AuthenticationFailed,
				StableReplies.AlreadyOnline),
			LoginFailure.NoTourneyPrivilege => Fail(LoginFailureReason.AuthenticationFailed,
				StableReplies.NoTourneyPrivilege),
			_ => Fail(LoginFailureReason.AuthenticationFailed)
		};
	}

	private async Task<byte[]> WelcomeAsync(Session connection, CancellationToken ct)
	{
		var user = connection.Siblings;
		var now = clock.GetUtcNow();
		var effective = user.EffectivePrivileges(now);
		var privileges = StableView.Privileges(effective);
		var motd = (await sender.Send(new ReadSettings(), ct)).General.Motd;
		var friends = (await sender.Send(new ListFriends(connection, user.Record.Id), ct)).Users;
		var silenceSeconds = user.Record.Value.IsSilenced(now)
			? (int)(user.Record.Value.SilencedEnd!.Value - now).TotalSeconds
			: 0;
		var online = (await sender.Send(new ListOnlineUsers(null), ct)).Where(other => !ReferenceEquals(other, user))
			.ToList();
		var onlineStats = await sender.Send(new GetUsersStats([
			.. online.Select(other => (other.User, other.Stable?.Status.Detail?.Mode ?? GameMode.Standard))
		]), ct);
		var others = online.Select((other, index) => (User: other, Stats: onlineStats[index])).ToList();

		var welcome = Outbox.Build(writer =>
		{
			writer.ProtocolVersion(ProtocolVersion);
			writer.LoginReply(user.Record.Id);
			writer.Privileges(privileges);
			if (silenceSeconds > 0) writer.SilenceEnd(silenceSeconds);
			if (!string.IsNullOrEmpty(motd)) writer.Notification(motd);
			writer.FriendsList([.. friends.Select(friend => friend.Id)]);
			foreach (var channel in channels.VisibleTo(connection))
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

	private static (string Token, byte[] Reply) Fail(LoginFailureReason reason, string? notification = null)
	{
		return ("no", Outbox.Build(writer =>
		{
			if (notification is not null) writer.Notification(notification);
			writer.LoginReply((int)reason);
		}));
	}
}
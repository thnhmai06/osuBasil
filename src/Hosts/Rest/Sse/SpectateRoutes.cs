using Basil.Application.Users.Messages;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Objects.Connections;
using Basil.Application.Users.Objects.Events;
using Basil.Application.Users.Registry;
using Basil.Domain.Client;
using Basil.Domain.Users;
using Basil.Host.Rest.Http;
using Mediator;

namespace Basil.Host.Rest.Sse;

/// <summary>Live gameplay of a player: spectating as an Api connection.</summary>
internal static class SpectateRoutes
{
	/// <summary>The key of the root that follows a player.</summary>
	public static string KeyOf(int userId)
	{
		return "spectate:" + userId;
	}

	/// <summary>Maps the Spectate routes.</summary>
	public static void MapSpectate(this RouteGroupBuilder users)
	{
		users.MapGet("/{id:int}/spectate", SpectateAsync).RequireToken().Sse()
			.WithSummary("Stream a player who is online in game.")
			.WithDescription("""
			                 The response is streamed using Server-Sent Events. Opening it spectates the player as an osu! client would: the player sees a spectator, and the spectating privilege, blocks and restrictions apply. The stream only watches; it joins no chat channel.

			                 The first event is the snapshot with the player's profile and in-game status. After that a bundle arrives every second with the frames, score updates and status changes since the previous one. Data is up to about one second old.
			                 """);
	}

	private static async Task<IResult> SpectateAsync(HttpContext context, int id,
		UserRegistry registry, ISender sender)
	{
		if (await sender.Send(new GetUser(UserReference.ById(id)), context.RequestAborted) is not { } record) return Api.NotFound("No such user.");

		var host = registry.Of(record).Stable;
		if (host is not { IsOpen: true })
			return Api.Error(StatusCodes.Status409Conflict, "targetOffline", "The player is not in game.");

		var connection = context.Connection();
		var spectated = await sender.Send(new SpectateUser(connection, host), context.RequestAborted);
		if (spectated != ConnectionOutcome.Ok) return spectated.Refusal();

		var result = await context.StreamAsync(KeyOf(id), _ => Task.FromResult<(object, string?)>((new
		{
			user = new
			{
				id = record.Id, name = record.Value.Name, country = record.Value.Country.ToAcronym().ToLowerInvariant()
			},
			status = StatusOf(host.Status)
		}, null)), () => _ = sender.Send(new StopSpectating(connection, host)));
		if (result is not SseResult) await sender.Send(new StopSpectating(connection, host));
		return result;
	}

	internal static object StatusOf(PlayerStatus status)
	{
		return new
		{
			activity = status.Activity, text = status.Text, beatmapId = status.BeatmapId,
			mods = status.GameMods.ToString(),
			mode = status.GameMode
		};
	}

	internal static object BundleOf(SpectatorBundle bundle)
	{
		// Compact on purpose: one array per frame instead of an object per frame.
		return new
		{
			type = "frames", action = bundle.Action, sequence = bundle.Sequence,
			frames = bundle.ReplayFrames.Select(f => new object[]
				{ (byte)f.Keys, (byte)f.Drum, f.Position.X, f.Position.Y, (int)f.Time.TotalMilliseconds }),
			score = new
			{
				time = (int)bundle.ScoreFrame.Time.TotalMilliseconds, total = bundle.ScoreFrame.State.TotalScore,
				combo = bundle.ScoreFrame.State.CurrentCombo,
				maxCombo = bundle.ScoreFrame.State.MaxCombo, n300 = bundle.ScoreFrame.State.HitCounts.Num300,
				n100 = bundle.ScoreFrame.State.HitCounts.Num100,
				n50 = bundle.ScoreFrame.State.HitCounts.Num50, geki = bundle.ScoreFrame.State.HitCounts.NumGeki,
				katu = bundle.ScoreFrame.State.HitCounts.NumKatu,
				miss = bundle.ScoreFrame.State.HitCounts.NumMiss, hp = bundle.ScoreFrame.Health
			}
		};
	}
}

/// <summary>Feeds the frames of a player to the stream of their Api spectators.</summary>
internal sealed class SpectateFramesFeed(SseHub hub) : INotificationHandler<UserSpectatorFramesReceived>
{
	/// <inheritdoc />
	public ValueTask Handle(UserSpectatorFramesReceived notification, CancellationToken ct)
	{
		hub.Publish(SpectateRoutes.KeyOf(notification.Host.Record.Id), SpectateRoutes.BundleOf(notification.Bundle));
		return ValueTask.CompletedTask;
	}
}

/// <summary>Feeds the status changes of a player to the stream of their Api spectators.</summary>
internal sealed class SpectateStatusFeed(SseHub hub) : INotificationHandler<UserConnectionStatusChanged>
{
	/// <inheritdoc />
	public ValueTask Handle(UserConnectionStatusChanged notification, CancellationToken ct)
	{
		hub.Publish(SpectateRoutes.KeyOf(notification.Connection.Record.Id),
			new { type = "status", status = SpectateRoutes.StatusOf(notification.Status) });
		return ValueTask.CompletedTask;
	}
}

/// <summary>Ends the stream of an Api spectator when the spectating ends, for example when the player goes offline.</summary>
internal sealed class SpectateEnd(SseHub hub) : INotificationHandler<UserSpectatorLeft>
{
	/// <inheritdoc />
	public ValueTask Handle(UserSpectatorLeft notification, CancellationToken ct)
	{
		if (notification.Spectator is ApiConnection)
			hub.Close(SpectateRoutes.KeyOf(notification.Host.Record.Id), notification.Spectator);
		return ValueTask.CompletedTask;
	}
}
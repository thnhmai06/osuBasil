using Basil.Application.Users.Objects.Connections;
using Basil.Application.Users.Persistence;
using Basil.Application.Users.Objects;
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

	public static void MapSpectate(this RouteGroupBuilder users)
	{
		users.MapGet("/{id:int}/spectate", SpectateAsync).RequireToken().Sse()
			.WithSummary("Watch a player who is online in game.")
			.WithDescription("""
				The response is streamed using Server-Sent Events. Opening it spectates the player as an osu! client would: the player sees a spectator, and the spectating permission, blocks and restrictions apply. The stream only watches; it joins no chat channel.

				The first event is the snapshot with the player's profile and in-game status. After that a bundle arrives every second with the frames, score updates and status changes since the previous one. Data is up to about one second old.
				""");
	}

	private static async Task<IResult> SpectateAsync(HttpContext context, int id, IUserRepository users,
		UserRegistry registry)
	{
		if (await users.GetAsync(id, context.RequestAborted) is not { } record) return Api.NotFound("No such user.");

		var host = registry.Of(record).Bancho;
		if (host is not { IsOpen: true })
			return Api.Error(StatusCodes.Status409Conflict, "targetOffline", "The player is not in game.");

		var connection = context.Connection();
		var spectated = await host.ScopeTo(connection).SpectateAsync();
		if (!spectated.IsOk) return spectated.Refusal();

		var result = await context.StreamAsync(KeyOf(id), _ => Task.FromResult<(object, string?)>((new
		{
			user = new { id = record.Id, name = record.Value.Name, country = record.Value.Country.ToAcronym().ToLowerInvariant() },
			status = StatusOf(host.Status)
		}, null)), () => host.ScopeTo(connection).StopSpectating());
		if (result is not SseResult) host.ScopeTo(connection).StopSpectating();
		return result;
	}

	internal static object StatusOf(PlayerStatus status)
	{
		return new
		{
			activity = status.Activity, text = status.Text, beatmapId = status.BeatmapId, mods = status.GameMods.ToString(),
			mode = status.GameMode
		};
	}

	internal static object BundleOf(SpectatorBundle bundle)
	{
		// Compact on purpose: one array per frame instead of an object per frame.
		return new
		{
			type = "frames", action = bundle.Action, sequence = bundle.Sequence,
			frames = bundle.Frames.Select(f => new object[] { f.ButtonState, f.TaikoByte, f.X, f.Y, f.Time }),
			score = new
			{
				time = bundle.Score.Time, total = bundle.Score.TotalScore, combo = bundle.Score.CurrentCombo,
				maxCombo = bundle.Score.MaxCombo, n300 = bundle.Score.Num300, n100 = bundle.Score.Num100,
				n50 = bundle.Score.Num50, geki = bundle.Score.NumGeki, katu = bundle.Score.NumKatu,
				miss = bundle.Score.NumMiss, hp = bundle.Score.CurrentHp
			}
		};
	}
}

/// <summary>Feeds the frames of a player to the stream of their Api spectators.</summary>
internal sealed class SpectateFramesFeed(SseHub hub) : INotificationHandler<UserSpectatorFramesReceived>
{
	public ValueTask Handle(UserSpectatorFramesReceived notification, CancellationToken cancellationToken)
	{
		hub.Publish(SpectateRoutes.KeyOf(notification.Host.Record.Id), SpectateRoutes.BundleOf(notification.Bundle));
		return ValueTask.CompletedTask;
	}
}

/// <summary>Feeds the status changes of a player to the stream of their Api spectators.</summary>
internal sealed class SpectateStatusFeed(SseHub hub) : INotificationHandler<UserConnectionStatusChanged>
{
	public ValueTask Handle(UserConnectionStatusChanged notification, CancellationToken cancellationToken)
	{
		hub.Publish(SpectateRoutes.KeyOf(notification.Connection.Record.Id),
			new { type = "status", status = SpectateRoutes.StatusOf(notification.Status) });
		return ValueTask.CompletedTask;
	}
}

/// <summary>Ends the stream of an Api spectator when the spectating ends, for example when the player goes offline.</summary>
internal sealed class SpectateEnd(SseHub hub) : INotificationHandler<UserSpectatorLeft>
{
	public ValueTask Handle(UserSpectatorLeft notification, CancellationToken cancellationToken)
	{
		if (notification.Spectator is ApiConnection)
			hub.Close(SpectateRoutes.KeyOf(notification.Host.Record.Id), notification.Spectator);
		return ValueTask.CompletedTask;
	}
}

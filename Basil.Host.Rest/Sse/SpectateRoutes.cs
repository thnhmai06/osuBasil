using Basil.Application.Sessions.Messages;
using Basil.Application.Sessions.Objects.Events;
using Basil.Application.Sessions.Registry;
using Basil.Application.Users;
using Basil.Application.Users.Messages;
using Basil.Application.Sessions.Objects;
using Basil.Domain.Sessions;
using Basil.Domain.Game;
using Basil.Domain.Spectating;
using Basil.Domain.Users;
using Basil.Host.Rest.Http;
using Mediator;

namespace Basil.Host.Rest.Sse;

internal static class SpectateRoutes
{
	public static string KeyOf(int userId)
	{
		return "spectate:" + userId;
	}

	public static void MapSpectate(this RouteGroupBuilder users)
	{
		users.MapGet("/{id:int}/spectate", SpectateAsync).RequireToken().Sse()
			.WithSummary("Stream a player.")
			.WithDescription("""
			                 The response is streamed using Server-Sent Events. Opening it spectates the player as an osu! client would: the player sees a spectator, and the spectating privilege, blocks and restrictions apply. The stream only watches; it joins no chat channel.

			                 The first event is the snapshot with the player's profile and in-game status. After that a bundle arrives every second with the frames, score updates and status changes since the previous one.
			                 """);
	}

	private static async Task<IResult> SpectateAsync(HttpContext context, int id,
		SessionRegistry registry, ISender sender)
	{
		if (await sender.Send(new GetUser(id), context.RequestAborted) is not { } record)
			return Api.NotFound("No such user.");

		var host = registry.Of(record).Stable;
		if (host is not { IsOpen: true })
			return Api.Error(StatusCodes.Status409Conflict, "targetOffline", "The player is not in game.");

		var connection = context.Session();
		var spectated = await sender.Send(new SpectateUser(connection, host), context.RequestAborted);
		if (spectated != SessionAuthResult.Ok) return spectated.Refusal();

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

	internal static object StatusOf(UserStatus status)
	{
		return new
		{
			activity = status.Activity, text = status.Detail?.BeatmapReference.FullName ?? string.Empty,
			beatmapId = status.Detail?.BeatmapReference.Id,
			mods = (status.Detail?.Mods ?? GameMods.NoMod).ToString(),
			mode = status.Detail?.Mode ?? GameMode.Standard
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

internal sealed class SpectateFramesFeed(SseHub hub) : INotificationHandler<SpectatorFramesReceived>
{
	public ValueTask Handle(SpectatorFramesReceived notification, CancellationToken ct)
	{
		hub.Publish(SpectateRoutes.KeyOf(notification.Host.Record.Id), SpectateRoutes.BundleOf(notification.Bundle));
		return ValueTask.CompletedTask;
	}
}

internal sealed class SpectateStatusFeed(SseHub hub) : INotificationHandler<SessionStatusChanged>
{
	public ValueTask Handle(SessionStatusChanged notification, CancellationToken ct)
	{
		hub.Publish(SpectateRoutes.KeyOf(notification.Session.Record.Id),
			new { type = "status", status = SpectateRoutes.StatusOf(notification.Status) });
		return ValueTask.CompletedTask;
	}
}

internal sealed class SpectateEnd(SseHub hub) : INotificationHandler<SpectatorLeft>
{
	public ValueTask Handle(SpectatorLeft notification, CancellationToken ct)
	{
		if (notification.Spectator is ApiSession)
			hub.Close(SpectateRoutes.KeyOf(notification.Host.Record.Id), notification.Spectator);
		return ValueTask.CompletedTask;
	}
}
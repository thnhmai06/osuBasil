using Basil.Application.Contracts.Ports;
using Basil.Application.Contracts.Repositories;
using Basil.Application.Services.Operations.Replies;
using Basil.Domain.Beatmaps;
using Basil.Domain.Mechanics;
using Basil.Domain.Multiplayer.Runtime;

namespace Basil.Application.Services.Operations.Commands.Mp;

/// <summary>
///     Backs the <c>!mp</c> subcommands that control a round's flow: <c>map</c>, <c>mods</c>,
///     <c>set</c>, <c>start</c>, <c>timer</c>, <c>aborttimer</c>, and <c>abort</c>.
/// </summary>
public sealed class MatchFlowCommands(
	IRepository<int, Beatmap> beatmapsById,
	RoomCountdowns countdowns,
	ILocalizer localizer)
{
	/// <summary>Handles <c>!mp map &lt;beatmap id&gt;</c>.</summary>
	public async Task<string> MapAsync(Room room, IReadOnlyList<string> args, CancellationToken cancellationToken)
	{
		if (args.Count < 1 || !int.TryParse(args[0], out var beatmapId))
			return localizer.Get(MpReplies.MapUsage);

		var beatmap = await beatmapsById.LoadAsync(beatmapId, cancellationToken);
		if (beatmap is null) return localizer.Get(MpReplies.NoBeatmapWithId, beatmapId);

		room.ChangeBeatmap(beatmap.Md5);
		return localizer.Get(MpReplies.ChangedBeatmap, beatmap.Beatmapset.Artist, beatmap.Beatmapset.Title,
			beatmap.Version);
	}

	/// <summary>Handles <c>!mp mods &lt;mods&gt;|Freemod|None</c>.</summary>
	public string SetMods(Room room, IReadOnlyList<string> args)
	{
		if (args.Count < 1) return localizer.Get(MpReplies.ModsUsage);

		var text = string.Join(' ', args);
		switch (text)
		{
			case "None":
				room.ChangeFreemods(false);
				room.ChangeMods(GameMods.NoMod);
				return localizer.Get(MpReplies.DisabledFreemod);
			case "Freemod":
				room.ChangeFreemods(true);
				return localizer.Get(MpReplies.EnabledFreemod);
			default:
				var mods = ModsExtensions.FromModString(string.Concat(args));
				room.ChangeMods(mods);
				return localizer.Get(MpReplies.EnabledMods, mods);
		}
	}

	/// <summary>Handles <c>!mp set &lt;teammode 0-3&gt; [scoremode 0-3] [size 1-16]</c>.</summary>
	public string Set(Room room, IReadOnlyList<string> args)
	{
		if (args.Count < 1 || !int.TryParse(args[0], out var teamMode) || !Enum.IsDefined((GameTeamType)teamMode))
			return localizer.Get(MpReplies.SetUsage);

		room.ChangeTeamType((GameTeamType)teamMode);

		if (args.Count > 1 && int.TryParse(args[1], out var winCondition) &&
		    Enum.IsDefined((GameWinCondition)winCondition))
			room.Settings.WinCondition = (GameWinCondition)winCondition;

		var sizeSuffix = string.Empty;
		if (args.Count > 2 && int.TryParse(args[2], out var size) && size is >= 1 and <= 16)
		{
			room.Resize(size);
			sizeSuffix = $", size {size}";
		}

		return localizer.Get(MpReplies.ChangedMatchSettings, room.Settings.TeamType, room.Settings.WinCondition,
			sizeSuffix);
	}

	/// <summary>Handles <c>!mp start [seconds]</c>.</summary>
	public Task<string> StartAsync(Room room, IReadOnlyList<string> args, CancellationToken cancellationToken)
	{
		if (room.InProgress) return Task.FromResult(localizer.Get(MpReplies.MatchAlreadyInProgress));

		if (args.Count > 0 && int.TryParse(args[0], out var seconds) && seconds > 0)
		{
			countdowns.Start(room.Id, TimeSpan.FromSeconds(seconds), true);
			return Task.FromResult(localizer.Get(MpReplies.MatchStartsInSeconds, seconds));
		}

		room.Start();
		return Task.FromResult(localizer.Get(MpReplies.MatchStarted));
	}

	/// <summary>Handles <c>!mp timer [seconds]</c>.</summary>
	public string Timer(Room room, IReadOnlyList<string> args)
	{
		var seconds = 30;
		if (args.Count > 0 && (!int.TryParse(args[0], out seconds) || seconds <= 0))
			return localizer.Get(MpReplies.TimerUsage);

		countdowns.Start(room.Id, TimeSpan.FromSeconds(seconds), false);
		return localizer.Get(MpReplies.CountdownStarted, seconds);
	}

	/// <summary>Handles <c>!mp aborttimer</c>.</summary>
	public string AbortTimer(Room room)
	{
		countdowns.Cancel(room.Id);
		return localizer.Get(MpReplies.CountdownAborted);
	}

	/// <summary>Handles <c>!mp abort</c>.</summary>
	public string Abort(Room room)
	{
		if (!room.InProgress) return localizer.Get(MpReplies.MatchNotInProgress);

		room.Abort();
		return localizer.Get(MpReplies.AbortedMatch);
	}
}
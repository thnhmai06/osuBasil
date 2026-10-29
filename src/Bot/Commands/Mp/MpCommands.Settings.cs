using Basil.Application.Multiplayer.Runtime;
using Basil.Application.Multiplayer.Runtime.Rooms;
using Basil.Application.Users.Connections;
using Basil.Bot.Replies;
using Basil.Domain.Mechanics;

namespace Basil.Bot.Commands.Mp;

internal sealed partial class MpCommands
{
	// ── !mp settings ─────────────────────────────────────────────────────────────────────

	private static async Task<bool> SettingsAsync(CommandContext context, Room room,
		CancellationToken cancellationToken)
	{
		var replies = context.Replies;
		var lines = new List<string>
		{
			replies.Text(MpReplies.SettingsRoomName, new { name = room.Settings.Name, room = room.Id }),
			room.Settings.Beatmap is not { } beatmap
				? replies.Text(MpReplies.SettingsBeatmapNotSelected)
				: replies.Text(MpReplies.SettingsBeatmap, new { beatmap = beatmap.Id, name = beatmap.Name }),
			replies.Text(MpReplies.SettingsTeamMode,
				new
				{
					teamType = room.Settings.MatchSettings.TeamType.ToString(),
					winCondition = room.Settings.MatchSettings.WinCondition.ToString()
				})
		};

		if (ActiveModsLine(replies, room) is { } modsLine) lines.Add(modsLine);

		var creator = room.Authority.Creator;
		lines.Add(replies.Text(MpReplies.SettingsCreator, new { id = creator.Id, name = creator.Value.Name }));

		var occupied = room.Slots.Count(slot => slot.Player is not null);
		lines.Add(replies.Text(MpReplies.SettingsPlayers, new { count = occupied }));

		var showTeam = room.Settings.MatchSettings.TeamType is GameTeamType.TeamVs or GameTeamType.TagTeamVs;
		foreach (var slot in room.Slots)
		{
			if (slot.Player is not { } player) continue;
			lines.Add(SlotLine(replies, room, slot, player, showTeam));
		}

		await context.Say(string.Join('\n', lines), cancellationToken);
		return true;
	}

	private static string? ActiveModsLine(IReplies replies, Room room)
	{
		var activeMods = new List<string>();
		if (room.Settings.MatchSettings.Mods != GameMods.NoMod)
			activeMods.Add(room.Settings.MatchSettings.Mods.ToString());
		if (room.Settings.MatchSettings.Freemods) activeMods.Add(replies.Text(MpReplies.Freemod));
		return activeMods.Count > 0
			? replies.Text(MpReplies.SettingsActiveMods, new { mods = string.Join(", ", activeMods) })
			: null;
	}

	private static string SlotLine(IReplies replies, Room room, RoomSlot slot, BanchoConnection player, bool showTeam)
	{
		var tags = new List<string>();
		if (room.Authority.Host is not null && player == room.Authority.Host)
			tags.Add(replies.Text(MpReplies.HostTag));
		if (slot.Mods is { } slotMods && slotMods != GameMods.NoMod) tags.Add(slotMods.ToString());
		var tagText = tags.Count > 0
			? replies.Text(MpReplies.Tags, new { tags = string.Join(" / ", tags) })
			: string.Empty;
		var teamText = showTeam && slot.Team is { } team ? $"{team,-5} " : string.Empty;
		return replies.Text(MpReplies.SettingsSlot, new
		{
			slot = $"{slot.Index,2}",
			status = $"{SlotStatusText(replies, slot.Status),-10}",
			team = teamText,
			id = $"{player.Record.Id,6}",
			name = $"{player.Record.Value.Name,-16}",
			tags = tagText
		});
	}

	private static string SlotStatusText(IReplies replies, RoomSlotStatus? status)
	{
		return status switch
		{
			RoomSlotStatus.NotReady => replies.Text(MpReplies.SlotStatusNotReady),
			RoomSlotStatus.NoMap => replies.Text(MpReplies.SlotStatusNoMap),
			_ => status?.ToString() ?? string.Empty
		};
	}

	// ── !mp lock / unlock ─────────────────────────────────────────────────────────────────

	private static async Task<bool> SetLockedAsync(CommandContext context, Room room, bool locked,
		CancellationToken cancellationToken)
	{
		var outcome = await room.ScopeTo(context.By).Settings.SetLockedAsync(locked, cancellationToken);
		if (!outcome.IsOk)
		{
			await context.Say(MpReplies.Describe(context.Replies, outcome), cancellationToken);
			return false;
		}

		await context.Reply(locked ? MpReplies.LockedMatch : MpReplies.UnlockedMatch, cancellationToken);
		return true;
	}

	// ── !mp private ──────────────────────────────────────────────────────────────────────

	private static async Task<bool> PrivateAsync(CommandContext context, Room room, IReadOnlyList<string> args,
		CancellationToken cancellationToken)
	{
		var by = context.By;
		if (args.Count == 0)
		{
			await context.Reply(MpReplies.MatchIsPrivateNow,
				new { privacy = room.Settings.IsPrivate ? "private" : "public" }, cancellationToken);
			return true;
		}

		if (args[0] is not ("0" or "1"))
		{
			await context.Reply(MpReplies.PrivateUsage, cancellationToken);
			return false;
		}

		var outcome = await room.ScopeTo(by).Settings.ConfigureAsync(
			new RoomSettingsChange(IsPrivate: args[0] == "1"), cancellationToken);
		if (!outcome.IsOk)
		{
			await context.Say(MpReplies.Describe(context.Replies, outcome), cancellationToken);
			return false;
		}

		await context.Reply(args[0] == "1" ? MpReplies.MatchNowPrivate : MpReplies.MatchNowPublic,
			cancellationToken);
		return true;
	}

	// ── !mp size ─────────────────────────────────────────────────────────────────────────

	private static async Task<bool> SetSizeAsync(CommandContext context, Room room, IReadOnlyList<string> args,
		CancellationToken cancellationToken)
	{
		if (args.Count < 1 || !int.TryParse(args[0], out var size) || size is < 1 or > MaxRoomSize)
		{
			await context.Reply(MpReplies.SizeUsage, cancellationToken);
			return false;
		}

		var outcome = await room.ScopeTo(context.By).Settings.ConfigureAsync(new RoomSettingsChange(Size: size),
			cancellationToken);
		if (!outcome.IsOk)
		{
			await context.Say(MpReplies.Describe(context.Replies, outcome), cancellationToken);
			return false;
		}

		await context.Reply(MpReplies.ChangedMatchSize, new { size }, cancellationToken);
		return true;
	}

	// ── !mp name / password ──────────────────────────────────────────────────────────────

	private static async Task<bool> SetNameAsync(CommandContext context, Room room, IReadOnlyList<string> args,
		CancellationToken cancellationToken)
	{
		if (args.Count < 1)
		{
			await context.Reply(MpReplies.NameUsage, cancellationToken);
			return false;
		}

		var name = string.Join(' ', args);
		if (name.Length > MaxRoomNameLength) name = name[..MaxRoomNameLength];

		var outcome = await room.ScopeTo(context.By).Settings.ConfigureAsync(new RoomSettingsChange(name),
			cancellationToken);
		if (!outcome.IsOk)
		{
			await context.Say(MpReplies.Describe(context.Replies, outcome), cancellationToken);
			return false;
		}

		await context.Reply(MpReplies.RoomNameUpdated, new { name }, cancellationToken);
		return true;
	}

	private static async Task<bool> SetPasswordAsync(CommandContext context, Room room, IReadOnlyList<string> args,
		CancellationToken cancellationToken)
	{
		var by = context.By;
		var password = args.Count == 0 ? string.Empty : string.Join(' ', args);
		var outcome = await room.ScopeTo(by).Settings.ConfigureAsync(new RoomSettingsChange(Password: password),
			cancellationToken);
		if (!outcome.IsOk)
		{
			await context.Say(MpReplies.Describe(context.Replies, outcome), cancellationToken);
			return false;
		}

		await context.Reply(args.Count == 0 ? MpReplies.RemovedMatchPassword : MpReplies.ChangedMatchPassword,
			cancellationToken);
		return true;
	}

	// ── !mp set ──────────────────────────────────────────────────────────────────────────

	private static async Task<bool> SetAsync(CommandContext context, Room room, IReadOnlyList<string> args,
		CancellationToken cancellationToken)
	{
		if (!TryParseSetArguments(args, out var teamValue, out var winCondition, out var size))
		{
			await context.Reply(MpReplies.SetUsage, cancellationToken);
			return false;
		}

		var change = new RoomSettingsChange(TeamType: (GameTeamType)teamValue, WinCondition: winCondition,
			Size: size);
		var outcome = await room.ScopeTo(context.By).Settings.ConfigureAsync(change, cancellationToken);
		if (!outcome.IsOk)
		{
			await context.Say(MpReplies.Describe(context.Replies, outcome), cancellationToken);
			return false;
		}

		await context.Reply(MpReplies.ChangedMatchSettings,
			new
			{
				teamType = room.Settings.MatchSettings.TeamType.ToString(),
				winCondition = room.Settings.MatchSettings.WinCondition.ToString(),
				size = size?.ToString() ?? "none"
			}, cancellationToken);
		return true;
	}

	private static bool TryParseSetArguments(IReadOnlyList<string> args, out int teamValue,
		out GameWinCondition? winCondition, out int? size)
	{
		winCondition = null;
		size = null;
		teamValue = 0;
		if (args.Count < 1 || !int.TryParse(args[0], out teamValue) || teamValue is < 0 or > 3) return false;

		return TryParseWinCondition(args, out winCondition) && TryParseSize(args, out size);
	}

	private static bool TryParseWinCondition(IReadOnlyList<string> args, out GameWinCondition? winCondition)
	{
		winCondition = null;
		if (args.Count < 2) return true;
		if (!int.TryParse(args[1], out var winValue) || winValue is < 0 or > 3) return false;

		winCondition = (GameWinCondition)winValue;
		return true;
	}

	private static bool TryParseSize(IReadOnlyList<string> args, out int? size)
	{
		size = null;
		if (args.Count < 3) return true;
		if (!int.TryParse(args[2], out var parsedSize) || parsedSize is < 1 or > MaxRoomSize) return false;

		size = parsedSize;
		return true;
	}

	// ── !mp map ──────────────────────────────────────────────────────────────────────────

	private async Task<bool> SetMapAsync(CommandContext context, Room room, IReadOnlyList<string> args,
		CancellationToken cancellationToken)
	{
		if (args.Count < 1 || !int.TryParse(args[0], out var beatmapId))
		{
			await context.Reply(MpReplies.MapUsage, cancellationToken);
			return false;
		}

		GameMode? mode = null;
		if (args.Count > 1)
		{
			if (!int.TryParse(args[1], out var modeValue) || modeValue is < 0 or > 3)
			{
				await context.Reply(MpReplies.MapUsage, cancellationToken);
				return false;
			}

			mode = (GameMode)modeValue;
		}

		var beatmap = await beatmaps.GetAsync(beatmapId, cancellationToken);
		if (beatmap is null)
		{
			await context.Reply(MpReplies.NoBeatmapWithId, new { beatmap = beatmapId }, cancellationToken);
			return false;
		}

		var reference = new BeatmapReference(beatmap.Hash, beatmap.Id, beatmap.Name,
			beatmap.Metadata.Difficulty.Mode, beatmap);
		var outcome = await room.ScopeTo(context.By).Settings.ConfigureAsync(
			new RoomSettingsChange(Beatmap: reference, Mode: mode ?? beatmap.Metadata.Difficulty.Mode),
			cancellationToken);
		if (!outcome.IsOk)
		{
			await context.Say(MpReplies.Describe(context.Replies, outcome), cancellationToken);
			return false;
		}

		await context.Reply(MpReplies.ChangedBeatmap,
			new
			{
				artist = beatmap.Beatmapset.Metadata.Artist,
				title = beatmap.Beatmapset.Metadata.Title,
				version = beatmap.Metadata.Version
			}, cancellationToken);
		return true;
	}

	// ── !mp mods ─────────────────────────────────────────────────────────────────────────

	private static async Task<bool> SetModsAsync(CommandContext context, Room room, IReadOnlyList<string> args,
		CancellationToken cancellationToken)
	{
		var by = context.By;
		if (args.Count < 1)
		{
			await context.Reply(MpReplies.ModsUsage, cancellationToken);
			return false;
		}

		var before = room.Settings.MatchSettings.Mods;
		var wasFreemod = room.Settings.MatchSettings.Freemods;

		var mods = GameMods.NoMod;
		var freemod = false;
		foreach (var token in args)
		{
			if (token.Equals("None", StringComparison.OrdinalIgnoreCase))
				continue;

			if (token.Equals("Freemod", StringComparison.OrdinalIgnoreCase))
			{
				freemod = true;
				continue;
			}

			mods |= ModsExtensions.FromModString(token);
		}

		var outcome = await room.ScopeTo(by).Settings.ConfigureAsync(
			new RoomSettingsChange(Mods: mods, Freemods: freemod), cancellationToken);
		if (!outcome.IsOk)
		{
			await context.Say(MpReplies.Describe(context.Replies, outcome), cancellationToken);
			return false;
		}

		var after = room.Settings.MatchSettings.Mods;
		var isFreemod = room.Settings.MatchSettings.Freemods;
		await context.Say(DescribeModChange(context.Replies, before, after, wasFreemod, isFreemod), cancellationToken);
		return true;
	}

	private static string DescribeModChange(IReplies replies, GameMods before, GameMods after, bool wasFreemod,
		bool isFreemod)
	{
		var enabled = after & ~before;
		var disabled = before & ~after;
		var parts = new List<string>();

		if (enabled != GameMods.NoMod)
			parts.Add(replies.Text(MpReplies.EnabledMods, new { mods = enabled.ToString() }));
		if (disabled != GameMods.NoMod)
			parts.Add(replies.Text(MpReplies.DisabledMods, new { mods = disabled.ToString() }));
		if (wasFreemod && !isFreemod) parts.Add(replies.Text(MpReplies.DisabledFreemod));
		else if (!wasFreemod && isFreemod) parts.Add(replies.Text(MpReplies.EnabledFreemod));

		return parts.Count > 0 ? string.Join(", ", parts) : replies.Text(MpReplies.NoModChanges);
	}
}
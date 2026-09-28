using Basil.Application.Contracts.Ports;
using Basil.Application.Models.Multiplayer;
using Basil.Application.Services.Operations.Replies;

namespace Basil.Application.Services.Operations.Commands.Mp;

/// <summary>
///     Backs the <c>!mp</c> subcommands that read or change a room's own settings: <c>settings</c>,
///     <c>lock</c>/<c>unlock</c>, <c>private</c>, <c>name</c>, and <c>password</c>.
/// </summary>
public sealed class RoomSettingsCommands(ILocalizer localizer)
{
	/// <summary>Handles <c>!mp settings</c>.</summary>
	public string Settings(Room room)
	{
		var lines = new List<string>
		{
			localizer.Get(MpReplies.SettingsRoomName, room.Match.Name, room.Id),
			room.Beatmap is not { } beatmap
				? localizer.Get(MpReplies.SettingsBeatmapNotSelected)
				: localizer.Get(MpReplies.SettingsBeatmap, beatmap.Id,
					$"{beatmap.Beatmapset.Artist} - {beatmap.Beatmapset.Title} [{beatmap.Version}]"),
			localizer.Get(MpReplies.SettingsTeamMode, room.Settings.TeamType, room.Settings.WinCondition),
			localizer.Get(MpReplies.SettingsActiveMods, room.Settings.Mods),
			room.Creator is { } creator
				? localizer.Get(MpReplies.SettingsCreator, creator.Id, creator.Name)
				: string.Empty,
			localizer.Get(MpReplies.SettingsPlayers, room.Slots.Count(s => s.User is not null))
		};

		return string.Join('\n', lines.Where(l => l.Length > 0));
	}

	/// <summary>Handles <c>!mp lock</c> and <c>!mp unlock</c>.</summary>
	public string SetLocked(Room room, bool locked)
	{
		room.Slots.Locked = locked;
		return localizer.Get(locked ? MpReplies.LockedMatch : MpReplies.UnlockedMatch);
	}

	/// <summary>Handles <c>!mp private [0|1]</c>.</summary>
	public string Private(Room room, IReadOnlyList<string> args)
	{
		if (args.Count == 0)
			return localizer.Get(MpReplies.MatchIsPrivateNow, room.Match.IsVisible ? "not private" : "private");

		if (args[0] is not ("0" or "1"))
			return localizer.Get(MpReplies.PrivateUsage);

		room.Match.IsVisible = args[0] == "0";
		return localizer.Get(room.Match.IsVisible ? MpReplies.MatchNowPublic : MpReplies.MatchNowPrivate);
	}

	/// <summary>Handles <c>!mp name &lt;text&gt;</c>.</summary>
	public string Rename(Room room, IReadOnlyList<string> args)
	{
		if (args.Count == 0) return localizer.Get(MpReplies.NameUsage);

		var name = string.Join(' ', args);
		room.Name = name;
		return localizer.Get(MpReplies.RoomNameUpdated, name);
	}

	/// <summary>Handles <c>!mp password [text]</c>.</summary>
	public string ChangePassword(Room room, IReadOnlyList<string> args)
	{
		var password = args.Count > 0 ? string.Join(' ', args) : string.Empty;
		room.Password = password;
		return localizer.Get(password.Length == 0 ? MpReplies.RemovedMatchPassword : MpReplies.ChangedMatchPassword);
	}

	/// <summary>Handles <c>!mp size &lt;1-16&gt;</c>.</summary>
	public string Resize(Room room, IReadOnlyList<string> args)
	{
		if (args.Count < 1 || !int.TryParse(args[0], out var size) || size is < 1 or > 16)
			return localizer.Get(MpReplies.SizeUsage);

		room.Slots.Resize(size);
		return localizer.Get(MpReplies.ChangedMatchSize, size);
	}
}
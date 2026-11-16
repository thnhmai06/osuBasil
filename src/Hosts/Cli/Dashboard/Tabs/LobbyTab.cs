using Basil.Application.Multiplayer.Messages;
using Basil.Application.Multiplayer.Persistence;
using Basil.Application.Multiplayer.Registry;
using Basil.Application.Shared.Paging;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Registry;
using Basil.Domain.Users;
using Basil.Host.Cli.Dashboard.Shell;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using XenoAtom.Terminal.UI;

namespace Basil.Host.Cli.Dashboard.Tabs;

/// <summary>One room in the lobby table, open or closed.</summary>
/// <param name="Id">The room id (the match id).</param>
/// <param name="Name">The room name.</param>
/// <param name="Players">How many players sit in it.</param>
/// <param name="Size">How many slots are usable.</param>
/// <param name="State">Playing, waiting, locked or closed.</param>
/// <param name="Host">The host, or empty.</param>
/// <param name="Creator">The creator, or empty.</param>
/// <param name="Referees">The referees, as names.</param>
/// <param name="HostMask">The privilege flags of the host as a number, which colour its name.</param>
/// <param name="CreatorMask">The privilege flags of the creator as a number, which colour its name.</param>
/// <param name="Open">Whether the room is open now, so it can be changed.</param>
/// <param name="Locked">Whether nobody else can join.</param>
/// <param name="Slots">The seated players: slot number, name, whether host, and the slot's status.</param>
/// <param name="Rules">The settings of an open room; <see langword="null" /> for a closed one.</param>
internal sealed record RoomItem(int Id, string Name, int Players, int Size, string State, string Host,
	string Creator, string Referees, ulong HostMask, ulong CreatorMask, bool Open, bool Locked, IReadOnlyList<(int Index, string Player, bool Host, string Status)> Slots,
	RoomRules? Rules = null);

/// <summary>Reads the rooms: the open ones from memory, the closed ones from the match history.</summary>
internal sealed class LobbySource(IServiceProvider services) : IListSource<RoomItem>
{
	/// <inheritdoc />
	public async Task<IReadOnlyList<SectionData<RoomItem>>> LoadAsync(string query, int limit, CancellationToken ct)
	{
		var lobby = services.GetRequiredService<Lobby>();
		var text = query.Trim();
		bool Matches(string name, int id) => text.Length == 0 || name.Contains(text, StringComparison.OrdinalIgnoreCase) ||
		                                     text == id.ToString();
		var open = lobby.Rooms.Values.Where(room => Matches(room.Settings.Name, room.Id)).OrderBy(room => room.Id)
			.Select(room =>
			{
				var host = room.Authority.Host;
				return new RoomItem(room.Id, room.Settings.Name, room.Slots.Count(slot => slot.Player is not null),
					room.Slots.Count(slot => !slot.Locked),
					room.Rounds.InProgress ? "playing" : room.Slots.Locked ? "locked" : "waiting",
					host?.Record.Value.Name ?? string.Empty, room.Authority.Creator.Value.Name,
					string.Join(", ", room.Authority.Referees.Select(referee => referee.Value.Name)),
					host is null ? 0UL : (ulong)host.Record.Value.Privileges, (ulong)room.Authority.Creator.Value.Privileges, true,
					room.Slots.Locked,
					[
						.. room.Slots.Where(slot => slot.Player is not null).Select(slot => (slot.Index,
							slot.Player!.Record.Value.Name, ReferenceEquals(host, slot.Player.User),
							slot.Status?.ToString() ?? "open"))
					],
					new RoomRules(room.Settings.IsPrivate, room.Settings.Password.Length > 0,
						room.Settings.Beatmap?.Name ?? string.Empty, room.Settings.MatchSettings.Mode.ToString(),
						room.Settings.MatchSettings.Mods.ToString(), room.Settings.MatchSettings.Freemods,
						room.Settings.MatchSettings.TeamType.ToString(), room.Settings.MatchSettings.WinCondition.ToString()));
			}).ToList();
		var openIds = open.Select(room => room.Id).ToHashSet();
		var actor = await services.GetRequiredService<UserRegistry>().OperateAsync(ct);
		var (result, found) = await services.GetRequiredService<ISender>()
			.Send(new ListMatches(actor, new MatchQuery(true), new PageRequest(0, limit)), ct);
		var closed = found is null
			? []
			: found.Items.Where(match => !openIds.Contains(match.Id) && Matches(match.Value.Name, match.Id))
				.Select(match => new RoomItem(match.Id, match.Value.Name, 0, 0, "closed", string.Empty,
					match.Value.Creator.Value.Name, string.Empty, 0, (ulong)match.Value.Creator.Value.Privileges, false, false, [])).ToList();
		return
		[
			new SectionData<RoomItem>("Open", open, false),
			new SectionData<RoomItem>("Closed", closed, found?.HasNext ?? false)
		];
	}
}

/// <summary>The rooms: the open ones with what the creator and the referees can do, and the ones that ended.</summary>
internal sealed class LobbyTab : ITabView, ISearchable
{
	private const string Help = """
		Type part of the room name (ignoring case) or the exact room id.
		""";

	private readonly ListPage<RoomItem> _page;
	private bool _opened;

	/// <summary>Creates the tab.</summary>
	public LobbyTab(IServiceProvider services, UserDetails users, CommandActions actions, Action<string> openChat, CliOptions options,
		Glyphs glyphs, ShellKit kit, TimeProvider clock, Action<Action> post, ILogger logger)
	{
		var details = new RoomDetails(users, actions, openChat, kit.Dialogs);
		_page = new ListPage<RoomItem>("Lobby", Help,
		[
			new Column<RoomItem>("ID", 8, r => r.Id.ToString()) { Right = true },
			new Column<RoomItem>("Name", 0, r => r.Name) { Weight = 3 },
			new Column<RoomItem>("Players", 9, r => r.Open ? $"{r.Players}/{r.Size}" : "-") { Right = true },
			new Column<RoomItem>("State", 9, r => r.State, State),
			new Column<RoomItem>("Host", 0, r => r.Host, r => UserInk.Of((UserPrivileges)r.HostMask))
			{
				Link = r => r.Host.Length == 0 ? null : (r.Host, () => users.OfAsync(UserReference.ByName(r.Host)))
			},
			new Column<RoomItem>("Creator", 0, r => r.Creator, r => UserInk.Of((UserPrivileges)r.CreatorMask))
			{
				Link = r => (r.Creator, () => users.OfAsync(UserReference.ByName(r.Creator)))
			}
		], r => r.Id.ToString(), new LobbySource(services), details.Of,
			glyphs, kit, options.NoColor, clock, post, logger);
	}

	/// <inheritdoc />
	public Visual View => _page.View;

	/// <inheritdoc />
	public void Attach(TerminalApp app)
	{
		_page.Attach(app);
	}

	/// <inheritdoc />
	public void Activate()
	{
		_page.FocusList();
		if (_opened) return;

		_opened = true;
		_page.Refresh();
	}

	/// <inheritdoc />
	public void Tick()
	{
		_page.Tick();
	}

	/// <inheritdoc />
	public void Refresh()
	{
		_page.Refresh();
	}

	/// <inheritdoc />
	public void FocusSearch()
	{
		_page.FocusSearch();
	}

	/// <inheritdoc />
	public void ShowHelp()
	{
		_page.ShowHelp();
	}

	internal static Ink State(RoomItem room)
	{
		return room.State switch
		{
			"playing" => Ink.RoomPlaying,
			"locked" => Ink.RoomLocked,
			"closed" => Ink.RoomClosed,
			_ => Ink.RoomOpen
		};
	}
}

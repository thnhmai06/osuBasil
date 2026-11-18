using System.Globalization;
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

internal sealed record RoomItem(int Id, string Name, int Players, int Size, string State, string Host,
	string Creator, string Referees, ulong HostMask, ulong CreatorMask, bool Open, bool Locked, IReadOnlyList<(int Index, string Player, bool Host, string Status)> Slots,
	RoomRules? Rules = null, bool Countdown = false)
{
	public bool CanStartRound => Open && State != "playing" && Players > 0 && Rules is { Map.Length: > 0 };

	public bool CanStartTimer => !Countdown && CanStartRound;

	public bool CanAbortTimer => Open && Countdown;
}

internal sealed class LobbySource(IServiceProvider services) : IListSource<RoomItem>
{
	public async Task<IReadOnlyList<SectionData<RoomItem>>> LoadAsync(string query, int limit, CancellationToken ct)
	{
		var registry = services.GetRequiredService<UserRegistry>();
		var sender = services.GetRequiredService<ISender>();
		var parsed = RoomQuery.Parse(query);
		var actor = await registry.OperateAsync(ct);
		var found = await sender.Send(new ListRooms(actor, parsed, new PageRequest(0, limit)), ct);
		var open = new List<RoomItem>();
		foreach (var room in found.Items)
		{
			var host = room.Authority.Host;
			open.Add(new RoomItem(room.Id, room.Settings.Name, room.Slots.Count(slot => slot.Player is not null),
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
					room.Settings.MatchSettings.TeamType.ToString(), room.Settings.MatchSettings.WinCondition.ToString()),
				await sender.Send(new HasRoomCountdown(actor, room), ct)));
		}

		var openIds = open.Select(room => room.Id).ToHashSet();
		// A closed room has no host, players, status, mode or password any more, so only the name and the creator can match.
		var (_, closedFound) = await sender.Send(new ListMatches(actor, new MatchQuery(true), new PageRequest(0, limit)), ct);
		var closed = closedFound is null || parsed.Host is not null || parsed.Player is not null || parsed.Status is not null ||
		             parsed.Mode is not null || parsed.Password is not null
			? []
			: closedFound.Items.Where(match => !openIds.Contains(match.Id) && Matches(parsed, match.Id, match.Value.Name,
					match.Value.Creator))
				.Select(match => new RoomItem(match.Id, match.Value.Name, 0, 0, "closed", string.Empty,
					match.Value.Creator.Value.Name, string.Empty, 0, (ulong)match.Value.Creator.Value.Privileges, false, false, [])).ToList();
		return
		[
			new SectionData<RoomItem>("Open", open, found.HasNext),
			new SectionData<RoomItem>("Closed", closed, closedFound?.HasNext ?? false)
		];
	}

	private static bool Matches(RoomQuery query, int id, string name, UserRecord creator)
	{
		return (query.Text is not { Length: > 0 } text || name.Contains(text, StringComparison.OrdinalIgnoreCase) ||
		        text == id.ToString(CultureInfo.InvariantCulture)) &&
		       (query.Creator is not { Length: > 0 } who || creator.Value.Name.Contains(who, StringComparison.OrdinalIgnoreCase) ||
		        who == creator.Id.ToString(CultureInfo.InvariantCulture));
	}
}

internal sealed class LobbyTab : ITabView, ISearchable
{
	private const string Help = """
		Words match the room name (ignoring case) or the exact room id.
		host:bob         the host, by name or id
		creator:bob      the creator, by name or id
		player:bob       a player seated in it, by name or id
		status:open      open, locked or playing
		mode:0           the game mode, 0 to 3
		password:yes|no  with or without a password
		Example: status:open mode:0 cup
		Filters apply to open rooms; closed rooms match the words and creator:.
		""";

	private readonly ListPage<RoomItem> _page;
	private bool _opened;

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

	public Visual View => _page.View;

	public void Attach(TerminalApp app)
	{
		_page.Attach(app);
	}

	public void Activate()
	{
		_page.FocusList();
		if (_opened) return;

		_opened = true;
		_page.Refresh();
	}

	public void Tick()
	{
		_page.Tick();
	}

	public void Refresh()
	{
		_page.Refresh();
	}

	public void FocusSearch()
	{
		_page.FocusSearch();
	}

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

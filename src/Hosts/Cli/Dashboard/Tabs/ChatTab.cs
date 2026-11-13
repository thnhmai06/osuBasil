using Basil.Application.Chat.Messages;
using Basil.Application.Chat.Registry;
using Basil.Application.Multiplayer.Registry;
using Basil.Application.Users.Registry;
using Basil.Host.Cli.Dashboard.Data;
using Basil.Host.Cli.Dashboard.Shell;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Geometry;
using XenoAtom.Terminal.UI.Styling;

namespace Basil.Host.Cli.Dashboard.Tabs;

/// <summary>One entry of the channel list.</summary>
/// <param name="Name">The channel name, which is the id.</param>
/// <param name="Members">How many members it has, or -1 when unknown.</param>
internal sealed record ChannelItem(string Name, int Members);

/// <summary>One entry of the member list.</summary>
/// <param name="Name">The user name.</param>
/// <param name="Mark">The mark after the name: <c>(H)</c> for the host, <c>(P)</c> for a player in a round.</param>
internal sealed record MemberItem(string Name, string Mark);

/// <summary>
///     The chat: the channels on the left, the messages of the selected channel in the middle with a box to answer as
///     BasilBot, and the members on the right, grouped by role.
/// </summary>
internal sealed class ChatTab : ITabView, ISelectable
{
	private readonly Palette _palette;
	private readonly GroupedList<ChannelItem> _channels;
	private readonly ChatFeed _feed;
	private readonly Glyphs _glyphs;
	private readonly LogControl _log = new LogControl { MaxCapacity = 500 }.WrapText(true);
	private readonly ILogger _logger;
	private readonly GroupedList<MemberItem> _members;
	private readonly CliOptions _options;
	private readonly PromptEditor? _input;
	private readonly IServiceProvider _services;
	private readonly State<string> _title = new("Chat");
	private readonly Func<(int Columns, int Rows)> _size;
	private string _current = string.Empty;
	private long _seen;
	private TerminalApp? _app;

	/// <summary>Creates the tab.</summary>
	public ChatTab(IServiceProvider services, ChatFeed feed, CliOptions options, Glyphs glyphs, Palette palette,
		Func<(int Columns, int Rows)> size, ILogger logger)
	{
		_services = services;
		_feed = feed;
		_options = options;
		_glyphs = glyphs;
		_palette = palette;
		_size = size;
		_logger = logger;
		_channels = new GroupedList<ChannelItem>(
			[new Column<ChannelItem>("Channel", 0, c => c.Name), new Column<ChannelItem>("", 4, c => c.Members < 0 ? string.Empty : $"({c.Members})", _ => Ink.Muted)],
			c => c.Name, glyphs, options.NoColor);
		_members = new GroupedList<MemberItem>(
			[new Column<MemberItem>("Member", 0, m => m.Name), new Column<MemberItem>("", 4, m => m.Mark, _ => Ink.Accent)],
			m => m.Name, glyphs, options.NoColor);
		var left = new Group("Channels").Style(GroupStyle.Rounded).Content(new ScrollViewer(_channels)).Stretch();
		var rows = new List<RowDefinition> { new() { Height = GridLength.Star() } };
		var middle = new Grid().Columns(new ColumnDefinition { Width = GridLength.Star() });
		if (options.Editable)
		{
			_input = new PromptEditor().LineMode(PromptEditorLineMode.SingleLine).PromptMarkup(string.Empty)
				.Placeholder("Chatting as BasilBot").Accepted(Send);
			rows.Add(new RowDefinition { Height = GridLength.Auto });
		}

		middle.Rows([.. rows]).Cell(_log, 0, 0);
		if (_input is not null) middle.Cell(_input, 1, 0);
		var center = new Group().Style(GroupStyle.Rounded).Content(middle).TopLeftText(() => _title.Value).Stretch();
		var right = new Group("Members").Style(GroupStyle.Rounded).Content(new ScrollViewer(_members)).Stretch()
			.IsVisible(() => _size().Columns >= 100);
		View = new Grid()
			.Columns(new ColumnDefinition { Width = GridLength.Star(3) }, new ColumnDefinition { Width = GridLength.Star(6) },
				new ColumnDefinition { Width = GridLength.Star(3) })
			.Rows(new RowDefinition { Height = GridLength.Star() })
			.Cell(left, 0, 0).Cell(center, 0, 1).Cell(right, 0, 2);
		_channels.KeyHook = _ => false;
	}

	/// <inheritdoc />
	public Visual View { get; }

	/// <inheritdoc />
	public void Attach(TerminalApp app)
	{
		_app = app;
	}

	/// <inheritdoc />
	public void Activate()
	{
		Refresh();
		if (_input is not null) _app?.Focus(_input);
	}

	/// <inheritdoc />
	public void Tick()
	{
		var selected = _channels.HasSelection ? _channels.Selected!.Name : string.Empty;
		if (selected != _current)
		{
			_current = selected;
			_seen = 0;
			_log.Clear();
			_title.Value = _current.Length == 0 ? "Chat" : _current;
			Members();
		}

		var fresh = _feed.Since(_current, _seen, out var total);
		foreach (var line in fresh) _log.AppendMarkupLine(Format(line));
		_seen = total;
	}

	/// <inheritdoc />
	public void Refresh()
	{
		var lobby = _services.GetRequiredService<Lobby>();
		var general = _services.GetRequiredService<GeneralChannelRegistry>().All;
		var names = _feed.Channels().Union(general.Keys, StringComparer.OrdinalIgnoreCase)
			.Union(lobby.Rooms.Keys.Select(id => $"#mp_{id}"), StringComparer.OrdinalIgnoreCase).Order().ToList();
		ChannelItem Item(string name) => new(name,
			general.TryGetValue(name, out var channel) ? channel.Members.Count : -1);
		_channels.SetSections(
		[
			new Section<ChannelItem>("Channels", names.Count(n => IsGeneral(n)).ToString(), [.. names.Where(IsGeneral).Select(Item)]),
			new Section<ChannelItem>("Multiplayer", names.Count(n => n.StartsWith("#mp_")).ToString(), [.. names.Where(n => n.StartsWith("#mp_")).Select(Item)]),
			new Section<ChannelItem>("DMs", names.Count(n => !n.StartsWith('#')).ToString(), [.. names.Where(n => !n.StartsWith('#')).Select(Item)])
		]);
		Members();
	}

	/// <inheritdoc />
	public void Select(string id)
	{
		Refresh();
		_channels.Select(id);
	}

	private static bool IsGeneral(string name)
	{
		return name.StartsWith('#') && !name.StartsWith("#mp_");
	}

	private string Format(ChatLine line)
	{
		var ink = line.Author.Equals("BasilBot", StringComparison.OrdinalIgnoreCase) ? Ink.Accent : Ink.Text;
		return $"{_palette.Wrap(Ink.Muted, line.Time.ToLocalTime().ToString("H:mm"))} " +
		       $"{_palette.Wrap(ink, line.Author + ":")} {_palette.Wrap(Ink.Text, line.Text)}";
	}

	private void Members()
	{
		var referees = new List<MemberItem>();
		var players = new List<MemberItem>();
		var others = new List<MemberItem>();
		if (_current.StartsWith("#mp_") && int.TryParse(_current[4..], out var id) &&
		    _services.GetRequiredService<Lobby>().Rooms.TryGetValue(id, out var room))
		{
			var host = room.Authority.Host;
			referees.Add(new MemberItem(room.Authority.Creator.Value.Name, string.Empty));
			referees.AddRange(room.Authority.Referees.Select(r => new MemberItem(r.Value.Name, string.Empty)));
			players.AddRange(room.Slots.Where(slot => slot.Player is not null).Select(slot => new MemberItem(
				slot.Player!.Record.Value.Name,
				ReferenceEquals(host, slot.Player.User) ? "(H)" : room.Rounds.InProgress ? "(P)" : string.Empty)));
		}
		else if (_services.GetRequiredService<GeneralChannelRegistry>().Find(_current) is { } channel)
		{
			others.AddRange(channel.Members.Select(c => new MemberItem(c.User.Record.Value.Name, string.Empty))
				.DistinctBy(m => m.Name).OrderBy(m => m.Name));
		}

		_members.SetSections(
		[
			new Section<MemberItem>("Referees", referees.Count.ToString(), referees),
			new Section<MemberItem>("Players", players.Count.ToString(), players),
			new Section<MemberItem>("Others", others.Count.ToString(), others)
		]);
	}

	private void Send()
	{
		var text = _input?.Text?.Trim() ?? string.Empty;
		if (_input is not null) _input.Text = string.Empty;
		if (text.Length == 0 || _current.Length == 0) return;

		var channel = _current;
		_ = Task.Run(async () =>
		{
			try
			{
				var actor = await _services.GetRequiredService<UserRegistry>().OperateAsync(CancellationToken.None);
				if (!channel.StartsWith('#')) return;

				ChatTarget target = channel.StartsWith("#mp_") && int.TryParse(channel[4..], out var room)
					? new ChatTarget.Room(room)
					: new ChatTarget.General(channel);
				await _services.GetRequiredService<ISender>().Send(new PostChatMessage(actor, target, text));
			}
			catch (Exception exception)
			{
				_logger.LogWarning(exception, "The chat message could not be sent");
			}
		});
	}
}

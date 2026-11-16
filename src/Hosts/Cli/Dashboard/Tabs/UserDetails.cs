using System.Globalization;
using Basil.Application.Authentication.Persistence;
using Basil.Application.Multiplayer.Registry;
using Basil.Application.Shared.Paging;
using Basil.Application.Users.Messages;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Registry;
using Basil.Domain.Authentication;
using Basil.Domain.Users;
using Basil.Host.Cli.Dashboard.Shell;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;

namespace Basil.Host.Cli.Dashboard.Tabs;

/// <summary>
///     Builds the detail of an account and of a session, the ones every tab shows when the operator clicks a user's
///     name or a session; the navigation buttons open the related views inside the same frame.
/// </summary>
/// <remarks>The reads run off the interface thread; the frame shows a loading state until they end.</remarks>
internal sealed class UserDetails(IServiceProvider services, ConsoleTab console, CliOptions options, Dialogs dialogs,
	Glyphs glyphs, TimeProvider clock, Action<string> openChat)
{
	private const int SessionsShown = 20;

	/// <summary>Reads an account and builds its detail.</summary>
	/// <param name="reference">The account, by id or name.</param>
	public async Task<DetailView> OfAsync(UserReference reference)
	{
		var record = await services.GetRequiredService<ISender>().Send(new GetUser(reference));
		if (record is null)
			return new DetailView(reference.Name ?? reference.Id?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
				[("User", "not found", Ink.Warning)], []);

		services.GetRequiredService<UserRegistry>().Online.TryGetValue(record, out var online);
		return Of(UsersSource.Item(services.GetRequiredService<Lobby>(), record, online, clock.GetUtcNow()));
	}

	/// <summary>Builds the detail of an account that is already read.</summary>
	/// <param name="user">The account.</param>
	public DetailView Of(UserItem user)
	{
		var room = user.Room.Length == 0 ? "-" : user.Room;
		var managed = user.Managed.Length == 0 ? string.Empty : $" ({user.Managed})";
		var ink = UserInk.Of((UserPrivileges)user.Mask);
		var fields = new List<(string, string, Ink)>
		{
			("ID", user.Id.ToString(), Ink.Text), ("Name", user.Name, ink), ("Country", user.Country, Ink.Text),
			("Privileges", $"{user.Privileges} ({user.Mask})", ink),
			("Connections", user.Connections.Length == 0 ? "offline" : user.Connections, Ink.Info),
			("Room", room + managed, Ink.Text), ("Activity", user.Activity.Length == 0 ? "-" : user.Activity, Ink.Text)
		};
		var actions = new List<DetailAction> { DetailAction.OpenAsync("Sessions", () => SessionsAsync(user)) };
		// The bot is a system account: its sessions can be read, nothing can be done to it.
		if (!options.Editable || user.Id == BasilBot.Id) return new DetailView(user.Name, fields, actions);

		var id = user.Id;
		if (user.Connections.Length > 0) actions.Add(new DetailAction("Send message", () => openChat(user.Name)));
		actions.Add(user.Silenced
			? new DetailAction("Unsilence", () => console.RunCommand($"users {id} unsilence"))
			: new DetailAction("Silence", () => AskSilence(id)));
		actions.Add(new DetailAction("Kick", () => console.RunCommand($"users {id} sessions delete"),
			DetailActionKind.Destructive));
		return new DetailView(user.Name, fields, actions);
	}

	/// <summary>Builds the detail of a session.</summary>
	/// <param name="session">The session.</param>
	public DetailView Of(SessionItem session)
	{
		var fields = new List<(string, string, Ink)>
		{
			("Session", session.Id.ToString(CultureInfo.InvariantCulture), Ink.Text),
			("User", session.UserName, UserInk.Of((UserPrivileges)session.Mask)), ("Kind", session.Kind, Ink.Text),
			("Last seen", session.LastSeen, session.Live ? Ink.Success : Ink.Text),
			("Since", SessionItem.Time(session.Started), Ink.Text), ("IP", session.Ip, Ink.Text),
			("Client", session.Client.Length == 0 ? "-" : session.Client, Ink.Text)
		};
		if (session.EndReason.Length > 0) fields.Add(("Ended", session.EndReason, Ink.Text));
		var actions = new List<DetailAction>
		{
			DetailAction.OpenAsync("User", () => OfAsync(UserReference.ById(session.UserId)))
		};
		if (options.Editable && session.Live)
			actions.Add(new DetailAction("Close session",
				() => console.RunCommand($"users {session.UserId} sessions {session.Id} delete"),
				DetailActionKind.Destructive));
		return new DetailView(session.Id.ToString(CultureInfo.InvariantCulture), fields, actions);
	}

	// The newest sessions of the account, read once as the console command does, shown as the table of the Sessions tab.
	private async Task<DetailView> SessionsAsync(UserItem user)
	{
		var actor = await services.GetRequiredService<UserRegistry>().OperateAsync();
		var (outcome, page) = await services.GetRequiredService<ISender>().Send(new ListUserSessions(actor,
			UserReference.ById(user.Id), new PageRequest(0, SessionsShown), SessionState.Any, null));
		if (outcome != UserOutcome.Ok || page is null)
			return new DetailView("Sessions", [("Sessions", "not available", Ink.Warning)], []);

		var now = clock.GetUtcNow();
		var items = page.Items.Select(record => SessionItem.Of(record, now)).ToList();
		return new DetailView("Sessions", [], [], push => Table(items, push));
	}

	private Visual Table(IReadOnlyList<SessionItem> items, Action<DetailView> push)
	{
		var list = new GroupedList<SessionItem>(SessionColumns.Of(null), session => session.Id.ToString(CultureInfo.InvariantCulture),
			glyphs, options.NoColor, clock) { ActivateOnClick = true };
		list.SetSections([.. SessionItem.Kinds.Select(kind => GroupOf(kind, items))]);
		list.Activated += session => push(Of(session));
		return new ScrollViewer(list).Stretch();
	}

	private static Section<SessionItem> GroupOf(SessionType kind, IReadOnlyList<SessionItem> items)
	{
		var of = items.Where(item => item.Type == kind).ToList();
		return new Section<SessionItem>(SessionItem.KindName(kind), of.Count.ToString(CultureInfo.InvariantCulture), of);
	}

	private void AskSilence(int id)
	{
		dialogs.Form("Silence", [("Duration", "30m"), ("Reason", string.Empty)], values =>
		{
			var reason = values[1].Length == 0 ? string.Empty : $" --reason \"{values[1].Replace("\"", "'")}\"";
			console.RunCommand($"users {id} silence --duration {values[0]}{reason}");
		});
	}
}

using Basil.Domain.Users;
using Basil.Host.Cli.Dashboard.Shell;

namespace Basil.Host.Cli.Dashboard.Tabs;

/// <summary>The columns of a table of sessions, which the Sessions tab and the sessions of one user share.</summary>
internal static class SessionColumns
{
	/// <summary>Makes the columns.</summary>
	/// <param name="user">
	///     Reads the view a click on the name of the account opens; <see langword="null" /> for a table of one
	///     account's sessions, which has no column for the account.
	/// </param>
	public static IReadOnlyList<Column<SessionItem>> Of(Func<SessionItem, (string Title, Func<Task<DetailView>> Read)?>? user)
	{
		var columns = new List<Column<SessionItem>>();
		if (user is not null)
			columns.Add(new Column<SessionItem>("User", 0, s => s.UserName, s => UserInk.Of((UserPrivileges)s.Mask))
			{
				Link = user
			});
		columns.Add(new Column<SessionItem>("Last seen", 17, s => s.LastSeen, s => s.Live ? Ink.Success : Ink.Muted));
		columns.Add(new Column<SessionItem>("Since", 17, s => SessionItem.Time(s.Started), _ => Ink.Muted));
		columns.Add(new Column<SessionItem>("Client / IP", 0, s => s.Where) { Weight = 2 });
		return columns;
	}
}

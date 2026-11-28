using System.Globalization;
using Basil.Application.Chat.Registry;
using Basil.Domain.Users;
using Basil.Host.Cli.Commands;
using Basil.Host.Cli.Dashboard.Shell;
using Microsoft.Extensions.DependencyInjection;

namespace Basil.Host.Cli.Dashboard.Tabs;

internal sealed class ChannelDetails(IServiceProvider services, CommandActions actions, Dialogs dialogs)
{
	public DetailView Of(string name)
	{
		var create = new DetailAction("New channel", AskNew);
		if (services.GetRequiredService<GeneralChannelRegistry>().Find(name) is not { } channel)
			return new DetailView("Channels", [("Channel", "only general channels can be changed", Ink.Muted)],
				[create]);

		var data = channel.Record.Value;
		var members = channel.Members.Select(member => member.User).Distinct().Count();
		var fields = new List<(string, string, Ink)>
		{
			("FullName", channel.Name, Ink.Text), ("Topic", data.Topic.Length == 0 ? "-" : data.Topic, Ink.Text),
			("Read", data.ReadPrivileges.ToString(), Ink.Text), ("Write", data.WritePrivileges.ToString(), Ink.Text),
			("Auto join", data.AutoJoin ? "yes" : "no", Ink.Text), ("Visible", data.Visible ? "yes" : "no", Ink.Text),
			("Members", members.ToString(CultureInfo.InvariantCulture), Ink.Text)
		};
		var quoted = CommandActions.Quote(channel.Name);
		var list = new List<DetailAction>
		{
			new("Edit topic", () => dialogs.Form("Topic", [new FormField("Topic", data.Topic)],
				values => actions.Run($"channels {quoted} set --topic {CommandActions.Quote(values[0])}"))),
			new("Who can read", () => dialogs.Privileges("Privileges to read", data.ReadPrivileges,
				granted => actions.Run($"channels {quoted} set --readPrivileges {FlagNames.Join(typeof(UserPrivileges), (ulong)granted)}"))),
			new("Who can write", () => dialogs.Privileges("Privileges to write", data.WritePrivileges,
				granted => actions.Run($"channels {quoted} set --writePrivileges {FlagNames.Join(typeof(UserPrivileges), (ulong)granted)}"))),
			new(data.AutoJoin ? "Stop auto join" : "Auto join",
				() => actions.Run($"channels {quoted} set --autoJoin {(data.AutoJoin ? "false" : "true")}")),
			new(data.Visible ? "Hide" : "Show",
				() => actions.Run($"channels {quoted} set --visible {(data.Visible ? "false" : "true")}")),
			create,
			new("Delete channel", () => dialogs.Confirm("Delete channel",
				$"Delete channel {channel.Name}? It is closed, its {members} member(s) are removed from it and its settings " +
				"are deleted.", "Delete", () => actions.Run($"channels {quoted} delete")), DetailActionKind.Destructive)
		};
		return new DetailView(channel.Name, fields, list) { Reload = () => Task.FromResult(Of(name)) };
	}

	private void AskNew()
	{
		dialogs.Form("New channel", [new FormField("FullName (start with #)"), new FormField("Topic")], values =>
			actions.Run($"channels create --name {CommandActions.Quote(values[0])}" +
			            (values[1].Length > 0 ? $" --topic {CommandActions.Quote(values[1])}" : string.Empty)));
	}
}
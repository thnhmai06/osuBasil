using Basil.Domain.Mechanics;
using Basil.Domain.Users;
using Basil.Host.Cli.Commands;
using Basil.Host.Shared.Views;

namespace Basil.Host.Cli.Tests.Commands;

/// <summary>The console prints people-readable text, not JSON: aligned fields, tables, flag names, local times.</summary>
public sealed class OutputTests
{
	private static readonly ViewFormatter Utc = new(TimeZoneInfo.Utc);

	[Fact]
	public void Fields_AlignedLabelLines()
	{
		var output = new CollectedOutput();

		output.Fields([("id", "5"), ("username", "alice")]);

		Assert.Equal(["id:       5", "username: alice"], output.Lines.Select(line => line.Text));
	}

	[Fact]
	public void Table_WidestColumnsHeader()
	{
		var output = new CollectedOutput();

		output.Table(["id", "name"], [["1", "alice"], ["22", "bo"]]);

		Assert.Equal(["id  name", "1   alice", "22  bo"], output.Lines.Select(line => line.Text));
		Assert.Equal(OutputKind.Header, output.Lines[0].Kind);
	}

	[Fact]
	public void Bitmask_FollowedByFlags()
	{
		var mask = (ulong)(UserPrivileges.Player | UserPrivileges.Moderator);

		var text = ViewFormatter.Mask("privileges", mask);

		Assert.Equal($"{mask} (Player, Moderator)", text);
	}

	[Fact]
	public void ConnectionTypeMask_NamesKinds()
	{
		Assert.Equal("12 (Irc, Api)", ViewFormatter.Mask("types", 12));
	}

	[Fact]
	public void NonMaskNumber_StaysNumber()
	{
		Assert.Equal("12", ViewFormatter.Mask("limit", 12));
		Assert.Equal("0", ViewFormatter.Mask("privileges", 0));
	}

	[Fact]
	public void Times_FormatterZone()
	{
		var plusSeven = new ViewFormatter(TimeZoneInfo.CreateCustomTimeZone("x", TimeSpan.FromHours(7), "x", "x"));
		var moment = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

		Assert.Equal("2026-01-01 00:00:00", Utc.Time(moment));
		Assert.Equal("2026-01-01 07:00:00", plusSeven.Time(moment));
	}

	[Fact]
	public void View_ApiNamesDashes()
	{
		var output = new CollectedOutput();
		var view = new UserView(3, "alice", "vn", (ulong)UserPrivileges.Player, (ulong)UserPrivileges.Player, null);

		Utc.Print(output, view);

		Assert.Contains(output.Lines, line => line.Text == "id:                  3");
		Assert.Contains(output.Lines, line => line.Text.StartsWith("privileges:") && line.Text.EndsWith("(Player)"));
		Assert.Contains(output.Lines, line => line.Text == "silencedUntil:       -");
	}

	[Fact]
	public void UserList_NamesAndIds()
	{
		var output = new CollectedOutput();
		var room = new RoomView(1, 2, "R", false, false, false, false, new UserRef(7, "bob"), null, [], [], null,
			GameMode.Standard, "NoMod", false, GameTeamType.HeadToHead,
			GameWinCondition.Score, []);

		Utc.Print(output, room);

		Assert.Contains(output.Lines, line => line.Text.Contains("bob (7)"));
	}

	[Fact]
	public void Output_NotJson()
	{
		var output = new CollectedOutput();
		Utc.Print(output, new UserView(1, "a", "vn", 0, 0, null));

		Assert.DoesNotContain(output.Lines, line => line.Text.TrimStart().StartsWith('{') || line.Text.Contains("\":"));
	}

	[Fact]
	public void Refusal_SingleLineCode()
	{
		var output = new CollectedOutput();

		output.Error("notFound", "No such user.");

		Assert.Equal("error: notFound: No such user.", output.Text);
	}

	[Fact]
	public void Codes_HumanizedSentences()
	{
		Assert.Equal("Room closed.", CommandReporting.Humanize("roomClosed"));
	}
}
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
	public void Fields_are_aligned_label_colon_value_lines()
	{
		var output = new CollectedOutput();

		output.Fields([("id", "5"), ("username", "alice")]);

		Assert.Equal(["id:       5", "username: alice"], output.Lines.Select(line => line.Text));
	}

	[Fact]
	public void A_table_has_a_header_row_and_columns_as_wide_as_their_widest_cell()
	{
		var output = new CollectedOutput();

		output.Table(["id", "name"], [["1", "alice"], ["22", "bo"]]);

		Assert.Equal(["id  name", "1   alice", "22  bo"], output.Lines.Select(line => line.Text));
		Assert.Equal(OutputKind.Header, output.Lines[0].Kind);
	}

	[Fact]
	public void A_bitmask_is_followed_by_the_names_of_its_flags()
	{
		var mask = (ulong)(UserPermissions.ChatPost | UserPermissions.ChatPostPrivate);

		var text = ViewFormatter.Mask("permissions", mask);

		Assert.Equal($"{mask} (ChatPost, ChatPostPrivate)", text);
	}

	[Fact]
	public void A_connection_type_mask_names_the_kinds()
	{
		Assert.Equal("12 (Irc, Api)", ViewFormatter.Mask("types", 12));
	}

	[Fact]
	public void A_number_that_is_not_a_mask_stays_a_number()
	{
		Assert.Equal("12", ViewFormatter.Mask("limit", 12));
		Assert.Equal("0", ViewFormatter.Mask("permissions", 0));
	}

	[Fact]
	public void Times_are_shown_in_the_zone_of_the_formatter()
	{
		var plusSeven = new ViewFormatter(TimeZoneInfo.CreateCustomTimeZone("x", TimeSpan.FromHours(7), "x", "x"));
		var moment = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

		Assert.Equal("2026-01-01 00:00:00", Utc.Time(moment));
		Assert.Equal("2026-01-01 07:00:00", plusSeven.Time(moment));
	}

	[Fact]
	public void A_view_prints_its_fields_with_api_names_booleans_and_dashes_for_nothing()
	{
		var output = new CollectedOutput();
		var view = new UserView(3, "alice", "vn", (ulong)UserPermissions.ChatPost, (ulong)UserPermissions.ChatPost,
			null);

		Utc.Print(output, view);

		Assert.Contains(output.Lines, line => line.Text == "id:                   3");
		Assert.Contains(output.Lines, line => line.Text.StartsWith("permissions:") && line.Text.EndsWith("(ChatPost)"));
		Assert.Contains(output.Lines, line => line.Text == "restrictions:         -");
	}

	[Fact]
	public void A_list_of_users_prints_as_a_table_with_a_name_and_id_for_a_reference()
	{
		var output = new CollectedOutput();
		var room = new RoomView(1, 2, "R", true, false, false, false, false, new UserRef(7, "bob"), null, [], [], null,
			GameMode.Standard, "NoMod", false, GameTeamType.HeadToHead,
			GameWinCondition.Score, []);

		Utc.Print(output, room);

		Assert.Contains(output.Lines, line => line.Text.Contains("bob (7)"));
	}

	[Fact]
	public void No_output_is_json()
	{
		var output = new CollectedOutput();
		Utc.Print(output, new UserView(1, "a", "vn", 0, 0, null));

		Assert.DoesNotContain(output.Lines, line => line.Text.TrimStart().StartsWith('{') || line.Text.Contains("\":"));
	}

	[Fact]
	public void A_refusal_is_one_error_line_with_the_api_code()
	{
		var output = new CollectedOutput();

		output.Error("notFound", "No such user.");

		Assert.Equal("error: notFound: No such user.", output.Text);
	}

	[Fact]
	public void Codes_are_humanized_into_sentences()
	{
		Assert.Equal("Room closed.", CommandReporting.Humanize("roomClosed"));
	}
}
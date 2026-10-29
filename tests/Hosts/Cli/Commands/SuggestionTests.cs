using Basil.Host.Cli.Commands;

namespace Basil.Host.Cli.Tests.Commands;

/// <summary>What the command box offers and how it colours a typed line.</summary>
public sealed class SuggestionTests
{
	private sealed class Source : ISuggestionSource
	{
		public IEnumerable<string> Ids(string resource)
		{
			return resource switch { "users" => ["1", "12", "30"], "rooms" => ["5"], "channels" => ["#osu", "#lobby"], _ => [] };
		}
	}

	private static readonly CommandSuggestions Suggestions = new(CommandCatalogFactory.Create(), new Source());

	[Fact]
	public void The_first_word_completes_to_the_commands_that_start_with_it()
	{
		var (words, start, length) = Suggestions.Suggest("us");

		Assert.Equal(["users"], words);
		Assert.Equal(0, start);
		Assert.Equal(2, length);
	}

	[Fact]
	public void After_a_space_the_next_word_of_the_command_is_offered()
	{
		var (words, _, _) = Suggestions.Suggest("channels ");

		Assert.Contains("list", words);
		Assert.Contains("create", words);
	}

	[Fact]
	public void An_id_position_offers_the_ids_that_exist()
	{
		var (words, _, _) = Suggestions.Suggest("users 1");

		Assert.Equal(["1", "12"], words);
	}

	[Fact]
	public void A_channel_position_offers_the_channel_names()
	{
		var (words, _, _) = Suggestions.Suggest("channels #o");

		Assert.Equal(["#osu"], words);
	}

	[Fact]
	public void A_complete_command_offers_its_options_that_are_not_given_yet()
	{
		var (words, _, _) = Suggestions.Suggest("users create --username a ");

		Assert.Contains("--password", words);
		Assert.DoesNotContain("--username", words);
	}

	[Fact]
	public void Describe_gives_the_pattern_and_the_summary()
	{
		Assert.StartsWith("users list  ", Suggestions.Describe("users list"));
	}

	[Fact]
	public void Highlight_tells_commands_values_and_options_apart()
	{
		const string line = "users 5 set --away hello";
		var kinds = Suggestions.Highlight(line).Select(token => token.Kind).ToArray();

		Assert.Equal([TokenKind.Command, TokenKind.Value, TokenKind.Subcommand, TokenKind.Option, TokenKind.OptionValue], kinds);
	}

	[Fact]
	public void Highlight_gives_each_token_its_position_in_the_line()
	{
		var tokens = Suggestions.Highlight("users list");

		Assert.Equal(new Token(0, 5, TokenKind.Command), tokens[0]);
		Assert.Equal(new Token(6, 4, TokenKind.Subcommand), tokens[1]);
	}

	[Fact]
	public void A_word_no_command_accepts_is_unknown()
	{
		Assert.Equal(TokenKind.Unknown, Suggestions.Highlight("nonsense")[0].Kind);
	}
}

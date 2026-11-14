using Basil.Host.Cli.Commands;

namespace Basil.Host.Cli.Tests.Commands;

/// <summary>What the command box offers and how it colours a typed line.</summary>
public sealed class SuggestionTests
{
	private static readonly CommandCatalog Catalog = CommandCatalogFactory.Create();
	private static readonly CommandSuggestions Suggestions = new(Catalog, new Source());

	[Fact]
	public void FirstWord_CompletesCommands()
	{
		var (words, start, length) = Suggestions.Suggest("us");

		Assert.Equal(["users"], words);
		Assert.Equal(0, start);
		Assert.Equal(2, length);
	}

	[Fact]
	public void AfterSpace_NextWordOffered()
	{
		var (words, _, _) = Suggestions.Suggest("channels ");

		Assert.Contains("list", words);
		Assert.Contains("create", words);
	}

	[Fact]
	public void IdPosition_OffersExistingIds()
	{
		var (words, _, _) = Suggestions.Suggest("users 1");

		Assert.Equal(["1", "12"], words);
	}

	[Fact]
	public void ChannelPosition_OffersNames()
	{
		var (words, _, _) = Suggestions.Suggest("channels #o");

		Assert.Equal(["#osu"], words);
	}

	[Fact]
	public void CompleteCommand_OffersMissingOptions()
	{
		var (words, _, _) = Suggestions.Suggest("users create --username a ");

		Assert.Contains("--password", words);
		Assert.DoesNotContain("--username", words);
	}

	[Fact]
	public void Describe_PatternAndSummary()
	{
		Assert.StartsWith("users list  ", Suggestions.Describe("users list"));
	}

	[Fact]
	public void Described_CommandWordCarriesItsSummary()
	{
		var (items, start, length) = Suggestions.SuggestDescribed("users li");

		Assert.Equal([("list", Catalog.All.First(c => c.Pattern == "users list").Summary)], items);
		Assert.Equal((6, 2), (start, length));
	}

	[Fact]
	public void Described_OptionCarriesItsDescription()
	{
		var (items, _, _) = Suggestions.SuggestDescribed("users create --user");

		var option = Catalog.All.First(c => c.Pattern == "users create").Options.First(o => o.Name == "username");
		Assert.Equal([("--username", option.Description)], items);
	}

	[Fact]
	public void Described_IdTakesTheSummaryOfTheCommandItAddresses()
	{
		var (items, _, _) = Suggestions.SuggestDescribed("rooms ");

		Assert.Contains(("5", Catalog.All.First(c => c.Pattern == "rooms <id:int>").Summary), items);
	}

	[Fact]
	public void Described_WordsMatchSuggest()
	{
		var (items, start, length) = Suggestions.SuggestDescribed("channels ");
		var (words, wordStart, wordLength) = Suggestions.Suggest("channels ");

		Assert.Equal(words, items.Select(item => item.Word));
		Assert.Equal((wordStart, wordLength), (start, length));
	}

	[Fact]
	public void Highlight_TellsTokensApart()
	{
		const string line = "users 5 set --away hello";
		var kinds = Suggestions.Highlight(line).Select(token => token.Kind).ToArray();

		Assert.Equal(
			[TokenKind.Command, TokenKind.Value, TokenKind.Subcommand, TokenKind.Option, TokenKind.OptionValue], kinds);
	}

	[Fact]
	public void Highlight_TokenPositions()
	{
		var tokens = Suggestions.Highlight("users list");

		Assert.Equal(new Token(0, 5, TokenKind.Command), tokens[0]);
		Assert.Equal(new Token(6, 4, TokenKind.Subcommand), tokens[1]);
	}

	[Fact]
	public void UnacceptedWord_Unknown()
	{
		Assert.Equal(TokenKind.Unknown, Suggestions.Highlight("nonsense")[0].Kind);
	}

	private sealed class Source : ISuggestionSource
	{
		public IEnumerable<string> Ids(string resource)
		{
			return resource switch
			{
				"users" => ["1", "12", "30"], "rooms" => ["5"], "channels" => ["#osu", "#lobby"], _ => []
			};
		}
	}
}
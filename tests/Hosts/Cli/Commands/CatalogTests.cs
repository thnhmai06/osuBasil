using Basil.Host.Cli.Commands;

namespace Basil.Host.Cli.Tests.Commands;

/// <summary>Matching a typed line with a command, and hiding secrets: the parser's contract.</summary>
public sealed class CatalogTests
{
	private static readonly CommandCatalog Catalog = CommandCatalogFactory.Create();

	[Theory]
	[InlineData("users list", new[] { "users", "list" })]
	[InlineData("users create --username \"two words\" --password x", new[] { "users", "create", "--username", "two words", "--password", "x" })]
	[InlineData("  a   b  ", new[] { "a", "b" })]
	[InlineData("say \"a \\\"quoted\\\" word\"", new[] { "say", "a \"quoted\" word" })]
	public void Split_follows_the_quoting_rules(string line, string[] expected)
	{
		Assert.Equal(expected, CommandCatalog.Split(line));
	}

	[Fact]
	public void A_literal_word_beats_a_value_word()
	{
		var (parsed, _) = Catalog.Parse(["users", "create", "--username", "a", "--password", "b"]);

		Assert.Equal("users create", parsed!.Command.Pattern);
	}

	[Fact]
	public void A_number_after_users_is_the_id_of_an_account()
	{
		var (parsed, _) = Catalog.Parse(["users", "42"]);

		Assert.Equal("users <id:int>", parsed!.Command.Pattern);
		Assert.Equal("42", parsed.Words["id"]);
	}

	[Fact]
	public void A_word_that_is_not_a_number_is_not_an_account_id()
	{
		var (parsed, failure) = Catalog.Parse(["users", "nobody"]);

		Assert.Null(parsed);
		Assert.NotNull(failure);
	}

	[Fact]
	public void Options_are_read_with_a_space_or_an_equals_sign()
	{
		var (parsed, _) = Catalog.Parse(["users", "list", "--query=abc", "--limit", "5"]);

		Assert.Equal("abc", parsed!.Options["query"]);
		Assert.Equal("5", parsed.Options["limit"]);
	}

	[Fact]
	public void A_boolean_option_without_a_value_means_true_and_does_not_eat_the_next_option()
	{
		var (parsed, _) = Catalog.Parse(["channels", "create", "--name", "#a", "--autoJoin", "--visible", "false"]);

		Assert.Equal("true", parsed!.Options["autoJoin"]);
		Assert.Equal("false", parsed.Options["visible"]);
	}

	[Fact]
	public void The_short_names_of_users_create_work()
	{
		var (parsed, _) = Catalog.Parse(["users", "create", "-u", "name", "-p", "secret", "--admin"]);

		Assert.Equal("name", parsed!.Options["username"]);
		Assert.Equal("secret", parsed.Options["password"]);
		Assert.Equal("true", parsed.Options["admin"]);
	}

	[Fact]
	public void A_missing_required_option_is_named()
	{
		var (parsed, failure) = Catalog.Parse(["users", "create", "--username", "a"]);

		Assert.Null(parsed);
		Assert.Contains("--password", failure!.Message);
	}

	[Fact]
	public void An_unknown_option_is_refused()
	{
		var (parsed, failure) = Catalog.Parse(["users", "list", "--bogus", "1"]);

		Assert.Null(parsed);
		Assert.Contains("--bogus", failure!.Message);
	}

	[Fact]
	public void An_option_without_a_value_is_refused()
	{
		var (parsed, _) = Catalog.Parse(["users", "list", "--query"]);

		Assert.Null(parsed);
	}

	[Fact]
	public void Settings_set_takes_any_dotted_field()
	{
		var (parsed, _) = Catalog.Parse(["settings", "rateLimits", "set", "--chat.count", "5"]);

		Assert.Equal("5", parsed!.Options["chat.count"]);
	}

	[Fact]
	public void A_session_id_must_be_a_guid()
	{
		Assert.NotNull(Catalog.Parse(["users", "1", "sessions", Guid.NewGuid().ToString()]).Parsed);
		Assert.Null(Catalog.Parse(["users", "1", "sessions", "x"]).Parsed);
	}

	[Fact]
	public void Every_pattern_is_unique()
	{
		Assert.Equal(Catalog.All.Count, Catalog.All.Select(command => command.Pattern).Distinct().Count());
	}

	[Fact]
	public void Every_flag_option_that_the_api_names_is_in_camel_case()
	{
		Assert.All(Catalog.All.SelectMany(command => command.Options), option => Assert.True(char.IsLower(option.Name[0]), option.Name));
	}

	[Theory]
	[InlineData("users create --username a --password hunter2", "users create --username a --password ***")]
	[InlineData("users 3 set --password=hunter2 --away x", "users 3 set --password=*** --away x")]
	[InlineData("users create -u a -p hunter2", "users create -u a -p ***")]
	[InlineData("rooms 1 password --password hunter2", "rooms 1 password --password ***")]
	public void Secrets_are_hidden_wherever_a_command_is_recorded(string line, string expected)
	{
		Assert.Equal(expected, Redactor.Redact(Catalog, line));
		Assert.DoesNotContain("hunter2", Redactor.Redact(Catalog, line));
	}

	[Fact]
	public void A_word_that_looks_like_a_secret_value_but_is_a_command_word_is_kept()
	{
		Assert.Equal("users list --query password", Redactor.Redact(Catalog, "users list --query password"));
	}
}

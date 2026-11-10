using Basil.Host.Cli.Commands;

namespace Basil.Host.Cli.Tests.Commands;

/// <summary>Matching a typed line with a command, and hiding secrets: the parser's contract.</summary>
public sealed class CatalogTests
{
	private static readonly CommandCatalog Catalog = CommandCatalogFactory.Create();

	[Theory]
	[InlineData("users list", new[] { "users", "list" })]
	[InlineData("users create --username \"two words\" --password x",
		new[] { "users", "create", "--username", "two words", "--password", "x" })]
	[InlineData("  a   b  ", new[] { "a", "b" })]
	[InlineData("say \"a \\\"quoted\\\" word\"", new[] { "say", "a \"quoted\" word" })]
	public void Split_FollowsQuotingRules(string line, string[] expected)
	{
		Assert.Equal(expected, CommandCatalog.Split(line));
	}

	[Fact]
	public void LiteralWord_BeatsValueWord()
	{
		var (parsed, _) = Catalog.Parse(["users", "create", "--username", "a", "--password", "b"]);

		Assert.Equal("users create", parsed!.Command.Pattern);
	}

	[Fact]
	public void NumberAfterUsers_AccountId()
	{
		var (parsed, _) = Catalog.Parse(["users", "42"]);

		Assert.Equal("users <id:int>", parsed!.Command.Pattern);
		Assert.Equal("42", parsed.Words["id"]);
	}

	[Fact]
	public void NonNumericWord_NotAccountId()
	{
		var (parsed, failure) = Catalog.Parse(["users", "nobody"]);

		Assert.Null(parsed);
		Assert.NotNull(failure);
	}

	[Fact]
	public void Options_SpaceOrEquals()
	{
		var (parsed, _) = Catalog.Parse(["users", "list", "--query=abc", "--limit", "5"]);

		Assert.Equal("abc", parsed!.Options["query"]);
		Assert.Equal("5", parsed.Options["limit"]);
	}

	[Fact]
	public void BooleanWithoutValue_MeansTrue()
	{
		var (parsed, _) = Catalog.Parse(["channels", "create", "--name", "#a", "--autoJoin", "--visible", "false"]);

		Assert.Equal("true", parsed!.Options["autoJoin"]);
		Assert.Equal("false", parsed.Options["visible"]);
	}

	[Fact]
	public void UsersShortNames_CreateWork()
	{
		var (parsed, _) = Catalog.Parse(["users", "create", "-u", "name", "-p", "secret", "--admin"]);

		Assert.Equal("name", parsed!.Options["username"]);
		Assert.Equal("secret", parsed.Options["password"]);
		Assert.Equal("true", parsed.Options["admin"]);
	}

	[Fact]
	public void MissingRequiredOption_Named()
	{
		var (parsed, failure) = Catalog.Parse(["users", "create", "--username", "a"]);

		Assert.Null(parsed);
		Assert.Contains("--password", failure!.Message);
	}

	[Fact]
	public void UnknownOption_Refused()
	{
		var (parsed, failure) = Catalog.Parse(["users", "list", "--bogus", "1"]);

		Assert.Null(parsed);
		Assert.Contains("--bogus", failure!.Message);
	}

	[Fact]
	public void ValuelessOption_Refused()
	{
		var (parsed, _) = Catalog.Parse(["users", "list", "--query"]);

		Assert.Null(parsed);
	}

	[Fact]
	public void SettingsSet_AnyDottedField()
	{
		var (parsed, _) = Catalog.Parse(["settings", "rateLimits", "set", "--chat.count", "5"]);

		Assert.Equal("5", parsed!.Options["chat.count"]);
	}

	[Fact]
	public void SessionId_MustBeNumber()
	{
		Assert.NotNull(Catalog.Parse(["users", "1", "sessions", "12345"]).Parsed);
		Assert.Null(Catalog.Parse(["users", "1", "sessions", "x"]).Parsed);
	}

	[Fact]
	public void Patterns_Unique()
	{
		Assert.Equal(Catalog.All.Count, Catalog.All.Select(command => command.Pattern).Distinct().Count());
	}

	[Fact]
	public void FlagOptions_CamelCase()
	{
		Assert.All(Catalog.All.SelectMany(command => command.Options),
			option => Assert.True(char.IsLower(option.Name[0]), option.Name));
	}

	[Theory]
	[InlineData("users create --username a --password hunter2", "users create --username a --password ***")]
	[InlineData("users 3 set --password=hunter2 --away x", "users 3 set --password=*** --away x")]
	[InlineData("users create -u a -p hunter2", "users create -u a -p ***")]
	[InlineData("rooms 1 password --password hunter2", "rooms 1 password --password ***")]
	public void RecordedCommands_SecretsHidden(string line, string expected)
	{
		Assert.Equal(expected, Redactor.Redact(Catalog, line));
		Assert.DoesNotContain("hunter2", Redactor.Redact(Catalog, line));
	}

	[Fact]
	public void SecretValueLookalike_Kept()
	{
		Assert.Equal("users list --query password", Redactor.Redact(Catalog, "users list --query password"));
	}
}
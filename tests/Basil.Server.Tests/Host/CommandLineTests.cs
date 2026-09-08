using Basil.Server.Host;

namespace Basil.Server.Tests.Host;

/// <summary>Verifies which arguments the executable treats as a command instead of as settings.</summary>
public class CommandLineTests
{
	public static TheoryData<string> CommandFlags => ["--version", "-v", "--help", "-h"];

	[Theory]
	[MemberData(nameof(CommandFlags))]
	public async Task ACommandFlagIsHandledInsteadOfStartingTheServer(string flag)
	{
		Assert.True(await RunCapturingOutput([flag]) is { Handled: true });
	}

	[Fact]
	public async Task ArgumentsThatNameNoCommandAreLeftForConfiguration()
	{
		var result = await RunCapturingOutput(["--Basil:Server:Port=8080", "--environment", "Development"]);

		Assert.False(result.Handled);
		Assert.Empty(result.Output);
	}

	[Fact]
	public async Task VersionReportsTheRunningVersion()
	{
		var result = await RunCapturingOutput(["--version"]);

		Assert.Contains("Basil", result.Output);
	}

	[Fact]
	public async Task HelpListsEveryCommandUnderBothOfItsNames()
	{
		var result = await RunCapturingOutput(["--help"]);

		foreach (var flag in new[] { "--update", "-u", "--version", "-v", "--help", "-h" })
			Assert.Contains(flag, result.Output);
	}

	private static async Task<(bool Handled, string Output)> RunCapturingOutput(string[] args)
	{
		var original = Console.Out;
		await using var captured = new StringWriter();
		Console.SetOut(captured);
		try
		{
			var handled = await CommandLine.TryRunAsync(args);
			return (handled, captured.ToString());
		}
		finally
		{
			Console.SetOut(original);
		}
	}
}

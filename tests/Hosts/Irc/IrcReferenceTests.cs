using Basil.Host.Irc.Tests.Support;
using Basil.Testing.Clients;
using Microsoft.Extensions.Options;

namespace Basil.Host.Irc.Tests;

/// <summary>The IRC reference lists the commands the server answers and the capabilities it grants.</summary>
[Collection("IRC host")]
public sealed class IrcReferenceTests(IrcHostFixture fixture)
{
	public static TheoryData<string> ListedCommands()
	{
		// QUIT ends the connection, so no answer can be read after it.
		return new TheoryData<string>(IrcReference.Commands.Select(command => command.Command)
			.Where(command => command != "QUIT"));
	}

	[Theory]
	[MemberData(nameof(ListedCommands))]
	public async Task ACommandOfTheReference_IsNotUnknownToTheServer(string command)
	{
		using var irc = await fixture.Host.SignInAsync("Ref" + command);

		await irc.SendAsync(command);
		var received = await irc.BarrierAsync();

		Assert.DoesNotContain(received, line => IrcClient.CommandOf(line) == "421");
	}

	[Fact]
	public void TheReference_NamesTheServerThePortTheCapabilitiesAndEveryCommand()
	{
		var reference = new IrcReference(Options.Create(new IrcOptions { Domain = "basil.test", Port = 6670 }));

		var text = reference.Markdown();

		Assert.Contains("`irc.basil.test`", text);
		Assert.Contains("`6670`", text);
		foreach (var capability in IrcCommands.Capabilities) Assert.Contains($"`{capability}`", text);
		foreach (var (command, _, _) in IrcReference.Commands) Assert.Contains($"| `{command}` |", text);
	}
}

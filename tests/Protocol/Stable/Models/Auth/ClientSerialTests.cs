using Basil.Protocol.Game.Stable.Models.Client;

namespace Basil.Protocol.Stable.Tests.Models.Auth;

public class ClientSerialTests
{
	public static TheoryData<string, string, string> ValidSerials()
	{
		return new TheoryData<string, string, string>
		{
			{ "a|b", "a", "b" },
			{ "a|b|c", "a", "b|c" },
			{ "|b", "", "b" },
			{ "a|", "a", "" }
		};
	}

	public static TheoryData<string> Rejects()
	{
		return
		[
			"ab",
			""
		];
	}

	[Theory]
	[MemberData(nameof(ValidSerials))]
	public void DocumentedSerial_FirstPipeSplit(string text, string uninstallId, string diskSignature)
	{
		var serial = ClientSerial.Parse(text);

		Assert.Equal(new ClientSerial(uninstallId, diskSignature), serial);
	}

	[Theory]
	[MemberData(nameof(Rejects))]
	public void SerialWithoutPipe_False(string text)
	{
		Assert.False(ClientSerial.TryParse(text, out _));
	}

	[Fact]
	public void NullText_False()
	{
		Assert.False(ClientSerial.TryParse(null, out _));
	}

	[Theory]
	[MemberData(nameof(Rejects))]
	public void SerialWithoutPipe_FormatException(string text)
	{
		Assert.Throws<FormatException>(() => ClientSerial.Parse(text));
	}

	[Theory]
	[MemberData(nameof(ValidSerials))]
	public void SerialComponents_WireText(string text, string uninstallId, string diskSignature)
	{
		var serial = new ClientSerial(uninstallId, diskSignature);

		Assert.Equal(text, serial.ToString());
	}
}
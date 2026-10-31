using Basil.Protocol.Bancho.Models.Auth;

namespace Basil.Protocol.Bancho.Tests.Models.Auth;

/// <summary>
///     Verifies the client serial contract <c>uninstallId|diskSignature</c>: the text splits on the first
///     <c>|</c> only (so the signature may contain further pipes), empty halves are allowed, and
///     <see cref="ClientSerial.ToString" /> restores the wire text. Text without a <c>|</c> is rejected.
/// </summary>
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
		return new TheoryData<string>
		{
			"ab",
			""
		};
	}

	[Theory]
	[MemberData(nameof(ValidSerials))]
	public void Parse_DocumentedSerialText_SplitsOnTheFirstPipe(string text, string uninstallId, string diskSignature)
	{
		var serial = ClientSerial.Parse(text);

		Assert.Equal(new ClientSerial(uninstallId, diskSignature), serial);
	}

	[Theory]
	[MemberData(nameof(Rejects))]
	public void TryParse_SerialTextWithoutPipe_ReturnsFalse(string text)
	{
		Assert.False(ClientSerial.TryParse(text, out _));
	}

	[Fact]
	public void TryParse_NullText_ReturnsFalse()
	{
		Assert.False(ClientSerial.TryParse(null, out _));
	}

	[Theory]
	[MemberData(nameof(Rejects))]
	public void Parse_SerialTextWithoutPipe_ThrowsFormatException(string text)
	{
		Assert.Throws<FormatException>(() => ClientSerial.Parse(text));
	}

	[Theory]
	[MemberData(nameof(ValidSerials))]
	public void ToString_SerialOfComponents_BuildsTheWireText(string text, string uninstallId, string diskSignature)
	{
		var serial = new ClientSerial(uninstallId, diskSignature);

		Assert.Equal(text, serial.ToString());
	}
}
using System.Text;
using Basil.Protocol.Bancho.Models.Auth;
using Basil.Protocol.Bancho.Wire.Packets;

namespace Basil.Protocol.Bancho.Tests.Models.Auth;

/// <summary>
///     Verifies the login body contract: line 1 username, line 2 password md5, line 3
///     <c>osuversion|utc_offset|display_city|client_hash|pm_private</c>, and the login failure reply ids -1 to -8.
/// </summary>
public class LoginRequestBodyTests
{
	private const string Body =
		"user\n5f4dcc3b5aa765d61d8327deb882cf99\nb20190326|-5|1|abc:def:ghi:jkl:mno:|0\n";

	private static readonly LoginRequestBody Expected = new("user", "5f4dcc3b5aa765d61d8327deb882cf99",
		"b20190326", -5, true, new ClientHash("abc", "def", "ghi", "jkl", "mno"), false);

	[Fact]
	public void Parse_Text_ReadsEveryField()
	{
		Assert.Equal(Expected, LoginRequestBody.Parse(Body));
	}

	[Fact]
	public void Parse_Bytes_ReadsEveryField()
	{
		Assert.Equal(Expected, LoginRequestBody.Parse(Encoding.UTF8.GetBytes(Body)));
	}

	[Fact]
	public void Parse_WithoutTrailingNewline_ReadsEveryField()
	{
		Assert.Equal(Expected, LoginRequestBody.Parse(Body.TrimEnd('\n')));
	}

	[Fact]
	public void Parse_FlagsOne_AreTrue()
	{
		var body = LoginRequestBody.Parse("u\np\nb1|0|1|a:b:c:d:e:|1\n");

		Assert.True(body.DisplayCity);
		Assert.True(body.BlockNonFriendDms);
	}

	[Fact]
	public void Parse_FlagsZero_AreFalse()
	{
		var body = LoginRequestBody.Parse("u\np\nb1|0|0|a:b:c:d:e:|0\n");

		Assert.False(body.DisplayCity);
		Assert.False(body.BlockNonFriendDms);
	}

	[Theory]
	[InlineData("user\npass\n")]
	[InlineData("user\npass\nb1|0|1|a:b:c:d:e:\n")]
	[InlineData("user\npass\nb1|x|1|a:b:c:d:e:|0\n")]
	[InlineData("user\npass\nb1|0|1|a:b:c:d:e|0\n")]
	[InlineData("")]
	public void Parse_Malformed_ThrowsFormatException(string body)
	{
		Assert.Throws<FormatException>(() => LoginRequestBody.Parse(body));
	}

	[Fact]
	public void Parse_InvalidUtf8_ThrowsFormatException()
	{
		Assert.Throws<FormatException>(() => LoginRequestBody.Parse([0xFF, 0xFE]));
	}

	[Theory]
	[InlineData(LoginFailureReason.AuthenticationFailed, -1)]
	[InlineData(LoginFailureReason.OldClient, -2)]
	[InlineData(LoginFailureReason.Banned, -3)]
	[InlineData(LoginFailureReason.BannedAlternate, -4)]
	[InlineData(LoginFailureReason.ErrorOccurred, -5)]
	[InlineData(LoginFailureReason.NeedsSupporter, -6)]
	[InlineData(LoginFailureReason.PasswordReset, -7)]
	[InlineData(LoginFailureReason.RequiresVerification, -8)]
	public void LoginFailureReason_HasTheWireId(LoginFailureReason reason, int id)
	{
		Assert.Equal(id, (int)reason);
	}

	[Fact]
	public void LoginReply_Failure_WritesNegativeIdAsInt32()
	{
		var bytes = PacketWriter.Build(w => w.LoginReply((int)LoginFailureReason.BannedAlternate));

		Assert.Equal(Convert.FromHexString("05000004000000fcffffff"), bytes);
	}
}
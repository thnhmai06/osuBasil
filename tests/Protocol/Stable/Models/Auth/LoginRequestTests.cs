using System.Text;
using Basil.Domain.Client;
using Basil.Protocol.Stable.Models.Authentication;
using Basil.Protocol.Stable.Models.Client;
using Basil.Protocol.Stable.Wire.Packets;

namespace Basil.Protocol.Stable.Tests.Models.Auth;

/// <summary>
///     Verifies the login body contract: line 1 username, line 2 password md5, line 3
///     <c>osuversion|utc_offset|display_city|client_hash|pm_private</c>, and the login failure reply ids -1 to -8.
/// </summary>
public class LoginRequestTests
{
	private const string Body =
		"user\n5f4dcc3b5aa765d61d8327deb882cf99\nb20190326|-5|1|0123456789abcdef0123456789abcdef:1.2.3.:fedcba9876543210fedcba9876543210:00112233445566778899aabbccddeeff:ffeeddccbbaa99887766554433221100:|0\n";

	private static readonly LoginRequest Expected = new("user", "5f4dcc3b5aa765d61d8327deb882cf99",
		new OsuVersion(new DateOnly(2019, 3, 26), null, OsuVersionStream.Stable), -5, true,
		OsuFingerprintExtensions.Parse("0123456789abcdef0123456789abcdef:1.2.3.:fedcba9876543210fedcba9876543210:00112233445566778899aabbccddeeff:ffeeddccbbaa99887766554433221100:"), false);

	[Fact]
	public void Parse_Text_ReadsEveryField()
	{
		Assert.Equal(Expected, LoginRequest.Parse(Body));
	}

	[Fact]
	public void Parse_Bytes_ReadsEveryField()
	{
		Assert.Equal(Expected, LoginRequest.Parse(Encoding.UTF8.GetBytes(Body)));
	}

	[Fact]
	public void Parse_WithoutTrailingNewline_ReadsEveryField()
	{
		Assert.Equal(Expected, LoginRequest.Parse(Body.TrimEnd('\n')));
	}

	[Fact]
	public void Parse_FlagsOne_AreTrue()
	{
		var body = LoginRequest.Parse("u\np\nb20190326|0|1|0123456789abcdef0123456789abcdef:1.2.3.:fedcba9876543210fedcba9876543210:00112233445566778899aabbccddeeff:ffeeddccbbaa99887766554433221100:|1\n");

		Assert.True(body.DisplayCity);
		Assert.True(body.BlockNonFriendDms);
	}

	[Fact]
	public void Parse_FlagsZero_AreFalse()
	{
		var body = LoginRequest.Parse("u\np\nb20190326|0|0|0123456789abcdef0123456789abcdef:1.2.3.:fedcba9876543210fedcba9876543210:00112233445566778899aabbccddeeff:ffeeddccbbaa99887766554433221100:|0\n");

		Assert.False(body.DisplayCity);
		Assert.False(body.BlockNonFriendDms);
	}

	[Theory]
	[InlineData("user\npass\n")]
	[InlineData("user\npass\nb20190326|0|1|0123456789abcdef0123456789abcdef:1.2.3.:fedcba9876543210fedcba9876543210:00112233445566778899aabbccddeeff:ffeeddccbbaa99887766554433221100:\n")]
	[InlineData("user\npass\nb20190326|x|1|0123456789abcdef0123456789abcdef:1.2.3.:fedcba9876543210fedcba9876543210:00112233445566778899aabbccddeeff:ffeeddccbbaa99887766554433221100:|0\n")]
	[InlineData("user\npass\nb20190326|0|1|0123456789abcdef0123456789abcdef:1.2.3.:fedcba9876543210fedcba9876543210:00112233445566778899aabbccddeeff:ffeeddccbbaa99887766554433221100|0\n")]
	[InlineData("")]
	public void Parse_Malformed_ThrowsFormatException(string body)
	{
		Assert.Throws<FormatException>(() => LoginRequest.Parse(body));
	}

	[Fact]
	public void Parse_InvalidUtf8_ThrowsFormatException()
	{
		Assert.Throws<FormatException>(() => LoginRequest.Parse([0xFF, 0xFE]));
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
using Basil.Protocol.Bancho.Models.Multiplayer;
using Basil.Protocol.Bancho.Models.Replay;
using Basil.Protocol.Bancho.Wire.Packets;

namespace Basil.Protocol.Bancho.Tests.Wire.Packets;

/// <summary>
///     Verifies `ServerPacketWriter`'s wire output for each server packet type, byte-for-byte
///     against golden expected payloads.
/// </summary>
public class ServerPacketWriterTests
{
	private static RoomPacket MakeMatch()
	{
		var slots = new List<RoomSlotPacket>
		{
			new(4, 1, 64, 1001),
			new(8, 2, 16, 32)
		};
		for (var i = 0; i < 14; i++) slots.Add(new RoomSlotPacket(1, 0, 0, null));

		return new RoomPacket(
			7,
			true,
			64,
			"party",
			"secret",
			"Camellia - Exit This Earth's Atomosphere",
			33852,
			"60b725f10c9c85c70d97880dfe8191b3",
			slots,
			32,
			0,
			2,
			2,
			true,
			123_456);
	}

	private static byte[] Hex(string hex)
	{
		return Convert.FromHexString(hex);
	}

	private static byte[] WritePacket(Action<PacketWriter> write)
	{
		using var stream = new MemoryStream();
		write(new PacketWriter(stream));
		return stream.ToArray();
	}

	[Theory]
	[InlineData(0, "0500000400000000000000")]
	[InlineData(2147483647, "05000004000000ffffff7f")]
	public void LoginReply(int userId, string expectedHex)
	{
		Assert.Equal(Hex(expectedHex), WritePacket(writer => writer.LoginReply(userId)));
	}

	[Fact]
	public void SendMessage_WithContent()
	{
		var expected =
			Hex("070000280000000b05636d7975690b11776f616820776f6168206372617a7921210b086a61636f6269616e20000000");
		Assert.Equal(expected, WritePacket(writer => writer.SendMessage("cmyui", "woah woah crazy!!", "jacobian", 32)));
	}

	[Fact]
	public void SendMessage_AllEmpty()
	{
		var expected = Hex("0700000700000000000000000000");
		Assert.Equal(expected, WritePacket(writer => writer.SendMessage("", "", "", 0)));
	}

	[Fact]
	public void Pong()
	{
		Assert.Equal(Hex("08000000000000"), WritePacket(writer => writer.Pong()));
	}

	[Fact]
	public void ChangeUsername_WithNames()
	{
		var expected = Hex("090000160000000b14636d7975693e3e3e3e61626367616d6572333231");
		Assert.Equal(expected, WritePacket(writer => writer.ChangeUsername("cmyui", "abcgamer321")));
	}

	[Fact]
	public void ChangeUsername_Empty()
	{
		var expected = Hex("090000060000000b043e3e3e3e");
		Assert.Equal(expected, WritePacket(writer => writer.ChangeUsername("", "")));
	}

	[Fact]
	public void UserStats_WithValues()
	{
		var expected =
			Hex(
				"0b000056000000e9030000020b0667616d696e670b20363062373235663130633963383563373064393738383064666538313931623340000000004b4d1a00f85fd84900000000d6566c3fed0e0000680a3de3000000002a0000006320");
		var actual = WritePacket(writer => writer.UserStats(
			1001, 2, "gaming",
			"60b725f10c9c85c70d97880dfe8191b3", 64, 0, 1723723,
			1_238_917_112, 92.32, 3821, 3_812_428_392,
			42, 8291));
		Assert.Equal(expected, actual);
	}

	[Fact]
	public void UserStats_AllZero()
	{
		var expected =
			Hex(
				"0b00002e00000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000");
		var actual = WritePacket(writer => writer.UserStats(
			0, 0, "", "", 0, 0, 0,
			0, 0.0, 0, 0, 0, 0));
		Assert.Equal(expected, actual);
	}

	[Theory]
	[InlineData(0, "0c0000050000000000000000")]
	[InlineData(2147483647, "0c000005000000ffffff7f00")]
	public void Logout(int userId, string expectedHex)
	{
		Assert.Equal(Hex(expectedHex), WritePacket(writer => writer.Logout(userId)));
	}

	[Theory]
	[InlineData(0, "0d00000400000000000000")]
	[InlineData(2147483647, "0d000004000000ffffff7f")]
	public void SpectatorJoined(int userId, string expectedHex)
	{
		Assert.Equal(Hex(expectedHex), WritePacket(writer => writer.SpectatorJoined(userId)));
	}

	[Theory]
	[InlineData(0, "0e00000400000000000000")]
	[InlineData(2147483647, "0e000004000000ffffff7f")]
	public void SpectatorLeft(int userId, string expectedHex)
	{
		Assert.Equal(Hex(expectedHex), WritePacket(writer => writer.SpectatorLeft(userId)));
	}

	[Fact]
	public void SpectateFrames()
	{
		var raw = Hex("0300017261772d737065637461746f722d6672616d652d64617461");
		var expected = Hex("0f00001b0000000300017261772d737065637461746f722d6672616d652d64617461");
		Assert.Equal(expected, WritePacket(writer => writer.SpectateFrames(raw)));
	}

	[Fact]
	public void VersionUpdate()
	{
		Assert.Equal(Hex("13000000000000"), WritePacket(writer => writer.VersionUpdate()));
	}

	[Theory]
	[InlineData(0, "1600000400000000000000")]
	[InlineData(2147483647, "16000004000000ffffff7f")]
	public void SpectatorCantSpectate(int userId, string expectedHex)
	{
		Assert.Equal(Hex(expectedHex), WritePacket(writer => writer.SpectatorCantSpectate(userId)));
	}

	[Fact]
	public void GetAttention()
	{
		Assert.Equal(Hex("17000000000000"), WritePacket(writer => writer.GetAttention()));
	}

	[Theory]
	[InlineData("waowww", "180000080000000b0677616f777777")]
	[InlineData("", "1800000100000000")]
	public void Notification(string msg, string expectedHex)
	{
		Assert.Equal(Hex(expectedHex), WritePacket(writer => writer.Notification(msg)));
	}

	[Fact]
	public void UpdateMatch_SendPasswordTrue()
	{
		var expected =
			Hex(
				"1a0000db00000007000100400000000b0570617274790b067365637265740b2843616d656c6c6961202d2045786974205468697320456172746827732041746f6d6f7370686572653c8400000b2036306237323566313063396338356337306439373838306466653831393162330408010101010101010101010101010101020000000000000000000000000000e90300002000000020000000000202014000000010000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000040e20100");
		Assert.Equal(expected, WritePacket(writer => writer.UpdateMatch(MakeMatch())));
	}

	[Fact]
	public void UpdateMatch_SendPasswordFalse()
	{
		var expected =
			Hex(
				"1a0000d500000007000100400000000b0570617274790b000b2843616d656c6c6961202d2045786974205468697320456172746827732041746f6d6f7370686572653c8400000b2036306237323566313063396338356337306439373838306466653831393162330408010101010101010101010101010101020000000000000000000000000000e90300002000000020000000000202014000000010000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000040e20100");
		Assert.Equal(expected, WritePacket(writer => writer.UpdateMatch(MakeMatch(), false)));
	}

	[Fact]
	public void NewMatch()
	{
		var expected =
			Hex(
				"1b0000db00000007000100400000000b0570617274790b067365637265740b2843616d656c6c6961202d2045786974205468697320456172746827732041746f6d6f7370686572653c8400000b2036306237323566313063396338356337306439373838306466653831393162330408010101010101010101010101010101020000000000000000000000000000e90300002000000020000000000202014000000010000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000040e20100");
		Assert.Equal(expected, WritePacket(writer => writer.NewMatch(MakeMatch())));
	}

	[Theory]
	[InlineData(0, "1c00000400000000000000")]
	[InlineData(2147483647, "1c000004000000ffffff7f")]
	public void DisposeMatch(int matchId, string expectedHex)
	{
		Assert.Equal(Hex(expectedHex), WritePacket(writer => writer.DisposeMatch(matchId)));
	}

	[Fact]
	public void ToggleBlockNonFriendDm()
	{
		Assert.Equal(Hex("22000000000000"), WritePacket(writer => writer.ToggleBlockNonFriendDm()));
	}

	[Fact]
	public void MatchJoinSuccess()
	{
		var expected =
			Hex(
				"240000db00000007000100400000000b0570617274790b067365637265740b2843616d656c6c6961202d2045786974205468697320456172746827732041746f6d6f7370686572653c8400000b2036306237323566313063396338356337306439373838306466653831393162330408010101010101010101010101010101020000000000000000000000000000e90300002000000020000000000202014000000010000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000040e20100");
		Assert.Equal(expected, WritePacket(writer => writer.MatchJoinSuccess(MakeMatch())));
	}

	[Fact]
	public void MatchJoinFail()
	{
		Assert.Equal(Hex("25000000000000"), WritePacket(writer => writer.MatchJoinFail()));
	}

	[Theory]
	[InlineData(0, "2a00000400000000000000")]
	[InlineData(2147483647, "2a000004000000ffffff7f")]
	public void FellowSpectatorJoined(int userId, string expectedHex)
	{
		Assert.Equal(Hex(expectedHex), WritePacket(writer => writer.FellowSpectatorJoined(userId)));
	}

	[Theory]
	[InlineData(0, "2b00000400000000000000")]
	[InlineData(2147483647, "2b000004000000ffffff7f")]
	public void FellowSpectatorLeft(int userId, string expectedHex)
	{
		Assert.Equal(Hex(expectedHex), WritePacket(writer => writer.FellowSpectatorLeft(userId)));
	}

	[Fact]
	public void MatchStart()
	{
		var expected =
			Hex(
				"2e0000db00000007000100400000000b0570617274790b067365637265740b2843616d656c6c6961202d2045786974205468697320456172746827732041746f6d6f7370686572653c8400000b2036306237323566313063396338356337306439373838306466653831393162330408010101010101010101010101010101020000000000000000000000000000e90300002000000020000000000202014000000010000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000040e20100");
		Assert.Equal(expected, WritePacket(writer => writer.MatchStart(MakeMatch())));
	}

	[Fact]
	public void MatchScoreUpdate_WithValues()
	{
		var frame = new ScoreFrame
		{
			Time = 38242,
			Id = 28,
			Num300 = 320,
			Num100 = 48,
			Num50 = 2,
			NumGeki = 32,
			NumKatu = 8,
			NumMiss = 3,
			TotalScore = 492_392,
			CurrentCombo = 39,
			MaxCombo = 122,
			Perfect = false,
			CurrentHp = 245,
			TagByte = 0,
			ScoreV2 = false,
			ComboPortion = null,
			BonusPortion = null
		};
		var expected = Hex("3000001d000000629500001c400130000200200008000300688307007a00270000f50000");
		Assert.Equal(expected, WritePacket(writer => writer.MatchScoreUpdate(frame)));
	}

	[Fact]
	public void MatchScoreUpdate_AllZero()
	{
		var frame = new ScoreFrame
		{
			Time = 0,
			Id = 0,
			Num300 = 0,
			Num100 = 0,
			Num50 = 0,
			NumGeki = 0,
			NumKatu = 0,
			NumMiss = 0,
			TotalScore = 0,
			CurrentCombo = 0,
			MaxCombo = 0,
			Perfect = false,
			CurrentHp = 0,
			TagByte = 0,
			ScoreV2 = false,
			ComboPortion = 0.0,
			BonusPortion = 0.0
		};
		var expected = Hex("3000001d0000000000000000000000000000000000000000000000000000000000000000");
		Assert.Equal(expected, WritePacket(writer => writer.MatchScoreUpdate(frame)));
	}

	[Fact]
	public void MatchTransferHost()
	{
		Assert.Equal(Hex("32000000000000"), WritePacket(writer => writer.MatchTransferHost()));
	}

	[Fact]
	public void MatchAllPlayersLoaded()
	{
		Assert.Equal(Hex("35000000000000"), WritePacket(writer => writer.MatchAllPlayersLoaded()));
	}

	[Theory]
	[InlineData(0, "3900000400000000000000")]
	[InlineData(2147483647, "39000004000000ffffff7f")]
	public void MatchPlayerFailed(int slotId, string expectedHex)
	{
		Assert.Equal(Hex(expectedHex), WritePacket(writer => writer.MatchPlayerFailed(slotId)));
	}

	[Fact]
	public void MatchComplete()
	{
		Assert.Equal(Hex("3a000000000000"), WritePacket(writer => writer.MatchComplete()));
	}

	[Fact]
	public void MatchSkip()
	{
		Assert.Equal(Hex("3d000000000000"), WritePacket(writer => writer.MatchSkip()));
	}

	[Theory]
	[InlineData("#osu", "400000060000000b04236f7375")]
	[InlineData("", "4000000100000000")]
	public void ChannelJoin(string name, string expectedHex)
	{
		Assert.Equal(Hex(expectedHex), WritePacket(writer => writer.ChannelJoin(name)));
	}

	[Fact]
	public void ChannelInfo_WithValues()
	{
		var expected = Hex("410000140000000b04236f73750b0a6c6520746f70697175657b00");
		Assert.Equal(expected, WritePacket(writer => writer.ChannelInfo("#osu", "le topique", 123)));
	}

	[Fact]
	public void ChannelInfo_Empty()
	{
		var expected = Hex("4100000400000000000000");
		Assert.Equal(expected, WritePacket(writer => writer.ChannelInfo("", "", 0)));
	}

	[Theory]
	[InlineData("#osu", "420000060000000b04236f7375")]
	[InlineData("", "4200000100000000")]
	public void ChannelKick(string name, string expectedHex)
	{
		Assert.Equal(Hex(expectedHex), WritePacket(writer => writer.ChannelKick(name)));
	}

	[Fact]
	public void ChannelAutoJoin_WithValues()
	{
		var expected = Hex("430000140000000b04236f73750b0a6c6520746f70697175657b00");
		Assert.Equal(expected, WritePacket(writer => writer.ChannelAutoJoin("#osu", "le topique", 123)));
	}

	[Fact]
	public void ChannelAutoJoin_Empty()
	{
		var expected = Hex("4300000400000000000000");
		Assert.Equal(expected, WritePacket(writer => writer.ChannelAutoJoin("", "", 0)));
	}

	[Theory]
	[InlineData(0, "4700000400000000000000")]
	[InlineData(2147483647, "47000004000000ffffff7f")]
	public void BanchoPrivileges(int priv, string expectedHex)
	{
		Assert.Equal(Hex(expectedHex), WritePacket(writer => writer.Privileges(priv)));
	}

	[Fact]
	public void FriendsList_WithValues()
	{
		Assert.Equal(Hex("4800000e00000003000100000004000000e9030000"),
			WritePacket(writer => writer.FriendsList([1, 4, 1001])));
	}

	[Fact]
	public void FriendsList_Empty()
	{
		Assert.Equal(Hex("480000020000000000"), WritePacket(writer => writer.FriendsList([])));
	}

	[Theory]
	[InlineData(0, "4b00000400000000000000")]
	[InlineData(2147483647, "4b000004000000ffffff7f")]
	public void ProtocolVersion(int ver, string expectedHex)
	{
		Assert.Equal(Hex(expectedHex), WritePacket(writer => writer.ProtocolVersion(ver)));
	}

	[Fact]
	public void MainMenuIcon_WithValues()
	{
		var expected =
			Hex(
				"4c0000380000000b3668747470733a2f2f69636f6e2d75726c2e63612f612e706e677c68747470733a2f2f6f6e636c69636b2d75726c2e63612f612e706e67");
		Assert.Equal(expected,
			WritePacket(writer => writer.MainMenuIcon("https://icon-url.ca/a.png", "https://onclick-url.ca/a.png")));
	}

	[Fact]
	public void MainMenuIcon_Empty()
	{
		Assert.Equal(Hex("4c0000030000000b017c"), WritePacket(writer => writer.MainMenuIcon("", "")));
	}

	[Fact]
	public void Monitor()
	{
		Assert.Equal(Hex("50000000000000"), WritePacket(writer => writer.Monitor()));
	}

	[Theory]
	[InlineData(0, "5100000400000000000000")]
	[InlineData(2147483647, "51000004000000ffffff7f")]
	public void MatchPlayerSkipped(int userId, string expectedHex)
	{
		Assert.Equal(Hex(expectedHex), WritePacket(writer => writer.MatchPlayerSkipped(userId)));
	}

	[Fact]
	public void UserPresence_WithValues()
	{
		var expected = Hex("5300001a000000e90300000b05636d79756913261f6f122f42440b9fc22a000000");
		var actual = WritePacket(writer => writer.UserPresence(
			1001, "cmyui", -5, 38, 31,
			0, 43.768, -79.522, 42));
		Assert.Equal(expected, actual);
	}

	[Fact]
	public void UserPresence_AllZero()
	{
		var expected = Hex("530000140000000000000000180000000000000000000000000000");
		var actual = WritePacket(writer => writer.UserPresence(
			0, "", 0, 0, 0,
			0, 0.0, 0.0, 0));
		Assert.Equal(expected, actual);
	}

	[Theory]
	[InlineData(0, "5600000400000000000000")]
	[InlineData(2147483647, "56000004000000ffffff7f")]
	public void RestartServer(int ms, string expectedHex)
	{
		Assert.Equal(Hex(expectedHex), WritePacket(writer => writer.RestartServer(ms)));
	}

	[Fact]
	public void MatchInvite()
	{
		var expected =
			Hex(
				"580000400000000b05636d7975690b2c436f6d65206a6f696e206d792067616d653a205b6f73756d703a2f2f372f7365637265742070617274795d2e0b05636f766572e9030000");
		var actual = WritePacket(writer => writer.MatchInvite(
			1001, "cmyui", "Come join my game: [osump://7/secret party].", "cover"));
		Assert.Equal(expected, actual);
	}

	[Fact]
	public void ChannelInfoEnd()
	{
		Assert.Equal(Hex("59000000000000"), WritePacket(writer => writer.ChannelInfoEnd()));
	}

	[Theory]
	[InlineData("newpassword", "5b00000d0000000b0b6e657770617373776f7264")]
	[InlineData("", "5b00000100000000")]
	public void MatchChangePassword(string newPassword, string expectedHex)
	{
		Assert.Equal(Hex(expectedHex), WritePacket(writer => writer.MatchChangePassword(newPassword)));
	}

	[Theory]
	[InlineData(0, "5c00000400000000000000")]
	[InlineData(2147483647, "5c000004000000ffffff7f")]
	public void SilenceEnd(int delta, string expectedHex)
	{
		Assert.Equal(Hex(expectedHex), WritePacket(writer => writer.SilenceEnd(delta)));
	}

	[Theory]
	[InlineData(0, "5e00000400000000000000")]
	[InlineData(2147483647, "5e000004000000ffffff7f")]
	public void UserSilenced(int userId, string expectedHex)
	{
		Assert.Equal(Hex(expectedHex), WritePacket(writer => writer.UserSilenced(userId)));
	}

	[Theory]
	[InlineData(0, "5f00000400000000000000")]
	[InlineData(2147483647, "5f000004000000ffffff7f")]
	public void UserPresenceSingle(int userId, string expectedHex)
	{
		Assert.Equal(Hex(expectedHex), WritePacket(writer => writer.UserPresenceSingle(userId)));
	}

	[Fact]
	public void UserPresenceBundle_WithValues()
	{
		Assert.Equal(Hex("6000000e00000003000100000004000000e9030000"),
			WritePacket(writer => writer.UserPresenceBundle([1, 4, 1001])));
	}

	[Fact]
	public void UserPresenceBundle_Empty()
	{
		Assert.Equal(Hex("600000020000000000"), WritePacket(writer => writer.UserPresenceBundle([])));
	}

	[Theory]
	[InlineData("cover", "6400000d00000000000b05636f76657200000000")]
	[InlineData("", "6400000700000000000000000000")]
	public void UserDmBlocked(string target, string expectedHex)
	{
		Assert.Equal(Hex(expectedHex), WritePacket(writer => writer.UserDmBlocked(target)));
	}

	[Theory]
	[InlineData("cover", "6500000d00000000000b05636f76657200000000")]
	[InlineData("", "6500000700000000000000000000")]
	public void TargetSilenced(string target, string expectedHex)
	{
		Assert.Equal(Hex(expectedHex), WritePacket(writer => writer.TargetSilenced(target)));
	}

	[Fact]
	public void VersionUpdateForced()
	{
		Assert.Equal(Hex("66000000000000"), WritePacket(writer => writer.VersionUpdateForced()));
	}

	[Theory]
	[InlineData(0, "6700000400000000000000")]
	[InlineData(2147483647, "67000004000000ffffff7f")]
	public void SwitchServer(int t, string expectedHex)
	{
		Assert.Equal(Hex(expectedHex), WritePacket(writer => writer.SwitchServer(t)));
	}

	[Fact]
	public void AccountRestricted()
	{
		Assert.Equal(Hex("68000000000000"), WritePacket(writer => writer.AccountRestricted()));
	}

	[Theory]
	[InlineData("yoyoo rip rtx", "6900000f0000000b0d796f796f6f2072697020727478")]
	[InlineData("", "6900000100000000")]
	public void Rtx(string msg, string expectedHex)
	{
		Assert.Equal(Hex(expectedHex), WritePacket(writer => writer.Rtx(msg)));
	}

	[Fact]
	public void MatchAbort()
	{
		Assert.Equal(Hex("6a000000000000"), WritePacket(writer => writer.MatchAbort()));
	}

	[Theory]
	[InlineData("61.91.139.24", "6b00000e0000000b0c36312e39312e3133392e3234")]
	[InlineData("", "6b00000100000000")]
	public void SwitchTournamentServer(string ip, string expectedHex)
	{
		Assert.Equal(Hex(expectedHex), WritePacket(writer => writer.SwitchTournamentServer(ip)));
	}
}
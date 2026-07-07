using System.Buffers.Binary;
using Bancho.Protocol.Multiplayer;

namespace Bancho.Protocol.Packets;

/// <summary>
/// Server-to-client Bancho packet builders. Ported function-for-function from app/packets.py.
/// Every payload is assembled from <see cref="PacketWriter"/> primitives, then wrapped with the
/// 7-byte packet header via <see cref="PacketWriter.Wrap"/>.
/// </summary>
public static class ServerPacketWriter
{
    private static byte[] Concat(params ReadOnlySpan<byte[]> parts)
    {
        var length = 0;
        foreach (var part in parts)
        {
            length += part.Length;
        }

        var result = new byte[length];
        var offset = 0;
        foreach (var part in parts)
        {
            part.CopyTo(result.AsSpan(offset));
            offset += part.Length;
        }

        return result;
    }

    private static byte[] WriteMessagePayload(string sender, string text, string recipient, int senderId) =>
        Concat(
            PacketWriter.WriteString(sender),
            PacketWriter.WriteString(text),
            PacketWriter.WriteString(recipient),
            PacketWriter.WriteInt32(senderId));

    private static byte[] WriteChannelPayload(string name, string topic, int playerCount)
    {
        var result = new byte[PacketWriter.WriteString(name).Length + PacketWriter.WriteString(topic).Length + 2];
        var nameBytes = PacketWriter.WriteString(name);
        var topicBytes = PacketWriter.WriteString(topic);
        nameBytes.CopyTo(result, 0);
        topicBytes.CopyTo(result, nameBytes.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(nameBytes.Length + topicBytes.Length), (ushort)playerCount);
        return result;
    }

    public static byte[] WriteMatch(MatchPacketData match, bool sendPassword)
    {
        var parts = new List<byte[]>();

        var header = new byte[8];
        BinaryPrimitives.WriteUInt16LittleEndian(header, (ushort)match.Id);
        header[2] = (byte)(match.InProgress ? 1 : 0);
        header[3] = 0; // match type, always 0
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), (uint)match.Mods);
        parts.Add(header);

        parts.Add(PacketWriter.WriteString(match.Name));

        if (!string.IsNullOrEmpty(match.Password))
        {
            parts.Add(sendPassword ? PacketWriter.WriteString(match.Password) : [0x0B, 0x00]);
        }
        else
        {
            parts.Add([0x00]);
        }

        parts.Add(PacketWriter.WriteString(match.MapName));
        parts.Add(PacketWriter.WriteInt32(match.MapId));
        parts.Add(PacketWriter.WriteString(match.MapMd5));

        parts.Add([.. match.Slots.Select(s => (byte)s.Status)]);
        parts.Add([.. match.Slots.Select(s => (byte)s.Team)]);

        foreach (var slot in match.Slots)
        {
            if (slot.HasPlayer)
            {
                parts.Add(PacketWriter.WriteUInt32((uint)slot.PlayerId!.Value));
            }
        }

        parts.Add(PacketWriter.WriteUInt32((uint)match.HostId));
        parts.Add([(byte)match.Mode, (byte)match.WinCondition, (byte)match.TeamType, (byte)(match.FreeMods ? 1 : 0)]);

        if (match.FreeMods)
        {
            foreach (var slot in match.Slots)
            {
                parts.Add(PacketWriter.WriteUInt32((uint)slot.Mods));
            }
        }

        parts.Add(PacketWriter.WriteUInt32((uint)match.Seed));

        return Concat([.. parts]);
    }

    public static byte[] WriteScoreFrame(ScoreFrameData frame)
    {
        var result = new byte[29];
        var span = result.AsSpan();

        BinaryPrimitives.WriteInt32LittleEndian(span, frame.Time);
        span[4] = (byte)frame.Id;
        BinaryPrimitives.WriteUInt16LittleEndian(span[5..], (ushort)frame.Num300);
        BinaryPrimitives.WriteUInt16LittleEndian(span[7..], (ushort)frame.Num100);
        BinaryPrimitives.WriteUInt16LittleEndian(span[9..], (ushort)frame.Num50);
        BinaryPrimitives.WriteUInt16LittleEndian(span[11..], (ushort)frame.NumGeki);
        BinaryPrimitives.WriteUInt16LittleEndian(span[13..], (ushort)frame.NumKatu);
        BinaryPrimitives.WriteUInt16LittleEndian(span[15..], (ushort)frame.NumMiss);
        BinaryPrimitives.WriteInt32LittleEndian(span[17..], frame.TotalScore);
        BinaryPrimitives.WriteUInt16LittleEndian(span[21..], (ushort)frame.MaxCombo);
        BinaryPrimitives.WriteUInt16LittleEndian(span[23..], (ushort)frame.CurrentCombo);
        span[25] = (byte)(frame.Perfect ? 1 : 0);
        span[26] = (byte)frame.CurrentHp;
        span[27] = (byte)frame.TagByte;
        span[28] = (byte)(frame.ScoreV2 ? 1 : 0);

        if (!frame.ScoreV2)
        {
            return result;
        }

        var comboPortion = new byte[8];
        var bonusPortion = new byte[8];
        BinaryPrimitives.WriteDoubleLittleEndian(comboPortion, frame.ComboPortion ?? 0.0);
        BinaryPrimitives.WriteDoubleLittleEndian(bonusPortion, frame.BonusPortion ?? 0.0);
        return Concat(result, comboPortion, bonusPortion);
    }

    // packet id: 5
    public static byte[] LoginReply(int userId) =>
        PacketWriter.Wrap(ServerPackets.UserId, PacketWriter.WriteInt32(userId));

    // packet id: 7
    public static byte[] SendMessage(string sender, string msg, string recipient, int senderId) =>
        PacketWriter.Wrap(ServerPackets.SendMessage, WriteMessagePayload(sender, msg, recipient, senderId));

    // packet id: 8
    public static byte[] Pong() => PacketWriter.Wrap(ServerPackets.Pong, []);

    // packet id: 9 (deprecated)
    public static byte[] ChangeUsername(string oldName, string newName) =>
        PacketWriter.Wrap(ServerPackets.HandleIrcChangeUsername, PacketWriter.WriteString($"{oldName}>>>>{newName}"));

    // packet id: 11
    public static byte[] UserStats(
        int userId,
        int action,
        string infoText,
        string mapMd5,
        int mods,
        int mode,
        int mapId,
        long rankedScore,
        double accuracy,
        int plays,
        long totalScore,
        int globalRank,
        int pp)
    {
        if (pp > 0xFFFF)
        {
            // HACK: if pp is over osu!'s ingame cap, display it as ranked score instead.
            rankedScore = pp;
            pp = 0;
        }

        var payload = Concat(
            PacketWriter.WriteInt32(userId),
            [(byte)action],
            PacketWriter.WriteString(infoText),
            PacketWriter.WriteString(mapMd5),
            PacketWriter.WriteInt32(mods),
            [(byte)mode],
            PacketWriter.WriteInt32(mapId),
            WriteInt64(rankedScore),
            WriteFloat32((float)(accuracy / 100.0)),
            PacketWriter.WriteInt32(plays),
            WriteInt64(totalScore),
            PacketWriter.WriteInt32(globalRank),
            WriteUInt16((ushort)pp));

        return PacketWriter.Wrap(ServerPackets.UserStats, payload);
    }

    // packet id: 12
    public static byte[] Logout(int userId) =>
        PacketWriter.Wrap(ServerPackets.UserLogout, Concat(PacketWriter.WriteInt32(userId), [0]));

    // packet id: 13
    public static byte[] SpectatorJoined(int userId) =>
        PacketWriter.Wrap(ServerPackets.SpectatorJoined, PacketWriter.WriteInt32(userId));

    // packet id: 14
    public static byte[] SpectatorLeft(int userId) =>
        PacketWriter.Wrap(ServerPackets.SpectatorLeft, PacketWriter.WriteInt32(userId));

    // packet id: 15
    public static byte[] SpectateFrames(byte[] rawData) =>
        PacketWriter.Wrap(ServerPackets.SpectateFrames, rawData);

    // packet id: 19
    public static byte[] VersionUpdate() => PacketWriter.Wrap(ServerPackets.VersionUpdate, []);

    // packet id: 22
    public static byte[] SpectatorCantSpectate(int userId) =>
        PacketWriter.Wrap(ServerPackets.SpectatorCantSpectate, PacketWriter.WriteInt32(userId));

    // packet id: 23
    public static byte[] GetAttention() => PacketWriter.Wrap(ServerPackets.GetAttention, []);

    // packet id: 24
    public static byte[] Notification(string msg) =>
        PacketWriter.Wrap(ServerPackets.Notification, PacketWriter.WriteString(msg));

    // packet id: 26
    public static byte[] UpdateMatch(MatchPacketData match, bool sendPassword = true) =>
        PacketWriter.Wrap(ServerPackets.UpdateMatch, WriteMatch(match, sendPassword));

    // packet id: 27
    public static byte[] NewMatch(MatchPacketData match) =>
        PacketWriter.Wrap(ServerPackets.NewMatch, WriteMatch(match, sendPassword: true));

    // packet id: 28
    public static byte[] DisposeMatch(int matchId) =>
        PacketWriter.Wrap(ServerPackets.DisposeMatch, PacketWriter.WriteInt32(matchId));

    // packet id: 34
    public static byte[] ToggleBlockNonFriendDm() =>
        PacketWriter.Wrap(ServerPackets.ToggleBlockNonFriendDms, []);

    // packet id: 36
    public static byte[] MatchJoinSuccess(MatchPacketData match) =>
        PacketWriter.Wrap(ServerPackets.MatchJoinSuccess, WriteMatch(match, sendPassword: true));

    // packet id: 37
    public static byte[] MatchJoinFail() => PacketWriter.Wrap(ServerPackets.MatchJoinFail, []);

    // packet id: 42
    public static byte[] FellowSpectatorJoined(int userId) =>
        PacketWriter.Wrap(ServerPackets.FellowSpectatorJoined, PacketWriter.WriteInt32(userId));

    // packet id: 43
    public static byte[] FellowSpectatorLeft(int userId) =>
        PacketWriter.Wrap(ServerPackets.FellowSpectatorLeft, PacketWriter.WriteInt32(userId));

    // packet id: 46
    public static byte[] MatchStart(MatchPacketData match) =>
        PacketWriter.Wrap(ServerPackets.MatchStart, WriteMatch(match, sendPassword: true));

    // packet id: 48
    public static byte[] MatchScoreUpdate(ScoreFrameData frame) =>
        PacketWriter.Wrap(ServerPackets.MatchScoreUpdate, WriteScoreFrame(frame));

    // packet id: 50
    public static byte[] MatchTransferHost() => PacketWriter.Wrap(ServerPackets.MatchTransferHost, []);

    // packet id: 53
    public static byte[] MatchAllPlayersLoaded() => PacketWriter.Wrap(ServerPackets.MatchAllPlayersLoaded, []);

    // packet id: 57
    public static byte[] MatchPlayerFailed(int slotId) =>
        PacketWriter.Wrap(ServerPackets.MatchPlayerFailed, PacketWriter.WriteInt32(slotId));

    // packet id: 58
    public static byte[] MatchComplete() => PacketWriter.Wrap(ServerPackets.MatchComplete, []);

    // packet id: 61
    public static byte[] MatchSkip() => PacketWriter.Wrap(ServerPackets.MatchSkip, []);

    // packet id: 64
    public static byte[] ChannelJoin(string name) =>
        PacketWriter.Wrap(ServerPackets.ChannelJoinSuccess, PacketWriter.WriteString(name));

    // packet id: 65
    public static byte[] ChannelInfo(string name, string topic, int playerCount) =>
        PacketWriter.Wrap(ServerPackets.ChannelInfo, WriteChannelPayload(name, topic, playerCount));

    // packet id: 66
    public static byte[] ChannelKick(string name) =>
        PacketWriter.Wrap(ServerPackets.ChannelKick, PacketWriter.WriteString(name));

    // packet id: 67
    public static byte[] ChannelAutoJoin(string name, string topic, int playerCount) =>
        PacketWriter.Wrap(ServerPackets.ChannelAutoJoin, WriteChannelPayload(name, topic, playerCount));

    // packet id: 71
    public static byte[] BanchoPrivileges(int priv) =>
        PacketWriter.Wrap(ServerPackets.Privileges, PacketWriter.WriteInt32(priv));

    // packet id: 72
    public static byte[] FriendsList(IReadOnlyList<int> friends) =>
        PacketWriter.Wrap(ServerPackets.FriendsList, PacketWriter.WriteI32List(friends));

    // packet id: 75
    public static byte[] ProtocolVersion(int version) =>
        PacketWriter.Wrap(ServerPackets.ProtocolVersion, PacketWriter.WriteInt32(version));

    // packet id: 76
    public static byte[] MainMenuIcon(string iconUrl, string onclickUrl) =>
        PacketWriter.Wrap(ServerPackets.MainMenuIcon, PacketWriter.WriteString($"{iconUrl}|{onclickUrl}"));

    // packet id: 80 (deprecated)
    public static byte[] Monitor() => PacketWriter.Wrap(ServerPackets.Monitor, []);

    // packet id: 81
    public static byte[] MatchPlayerSkipped(int userId) =>
        PacketWriter.Wrap(ServerPackets.MatchPlayerSkipped, PacketWriter.WriteInt32(userId));

    // packet id: 83
    public static byte[] UserPresence(
        int userId,
        string name,
        int utcOffset,
        int countryCode,
        int banchoPrivileges,
        int mode,
        double longitude,
        double latitude,
        int globalRank)
    {
        var payload = Concat(
            PacketWriter.WriteInt32(userId),
            PacketWriter.WriteString(name),
            [(byte)(utcOffset + 24)],
            [(byte)countryCode],
            [(byte)(banchoPrivileges | (mode << 5))],
            WriteFloat32((float)longitude),
            WriteFloat32((float)latitude),
            PacketWriter.WriteInt32(globalRank));

        return PacketWriter.Wrap(ServerPackets.UserPresence, payload);
    }

    // packet id: 86
    public static byte[] RestartServer(int ms) =>
        PacketWriter.Wrap(ServerPackets.Restart, PacketWriter.WriteInt32(ms));

    // packet id: 88
    public static byte[] MatchInvite(int playerId, string playerName, string matchEmbed, string targetName)
    {
        var msg = $"Come join my game: {matchEmbed}.";
        return PacketWriter.Wrap(ServerPackets.MatchInvite, WriteMessagePayload(playerName, msg, targetName, playerId));
    }

    // packet id: 89
    public static byte[] ChannelInfoEnd() => PacketWriter.Wrap(ServerPackets.ChannelInfoEnd, []);

    // packet id: 91
    public static byte[] MatchChangePassword(string newPassword) =>
        PacketWriter.Wrap(ServerPackets.MatchChangePassword, PacketWriter.WriteString(newPassword));

    // packet id: 92
    public static byte[] SilenceEnd(int delta) =>
        PacketWriter.Wrap(ServerPackets.SilenceEnd, PacketWriter.WriteInt32(delta));

    // packet id: 94
    public static byte[] UserSilenced(int userId) =>
        PacketWriter.Wrap(ServerPackets.UserSilenced, PacketWriter.WriteInt32(userId));

    // packet id: 95 (unused by bancho.py, kept for parity)
    public static byte[] UserPresenceSingle(int userId) =>
        PacketWriter.Wrap(ServerPackets.UserPresenceSingle, PacketWriter.WriteInt32(userId));

    // packet id: 96 (unused by bancho.py, kept for parity)
    public static byte[] UserPresenceBundle(IReadOnlyList<int> userIds) =>
        PacketWriter.Wrap(ServerPackets.UserPresenceBundle, PacketWriter.WriteI32List(userIds));

    // packet id: 100
    public static byte[] UserDmBlocked(string target) =>
        PacketWriter.Wrap(ServerPackets.UserDmBlocked, WriteMessagePayload("", "", target, 0));

    // packet id: 101
    public static byte[] TargetSilenced(string target) =>
        PacketWriter.Wrap(ServerPackets.TargetIsSilenced, WriteMessagePayload("", "", target, 0));

    // packet id: 102
    public static byte[] VersionUpdateForced() => PacketWriter.Wrap(ServerPackets.VersionUpdateForced, []);

    // packet id: 103
    public static byte[] SwitchServer(int t) =>
        PacketWriter.Wrap(ServerPackets.SwitchServer, PacketWriter.WriteInt32(t));

    // packet id: 104
    public static byte[] AccountRestricted() => PacketWriter.Wrap(ServerPackets.AccountRestricted, []);

    // packet id: 105 (deprecated)
    public static byte[] Rtx(string msg) => PacketWriter.Wrap(ServerPackets.Rtx, PacketWriter.WriteString(msg));

    // packet id: 106
    public static byte[] MatchAbort() => PacketWriter.Wrap(ServerPackets.MatchAbort, []);

    // packet id: 107
    public static byte[] SwitchTournamentServer(string ip) =>
        PacketWriter.Wrap(ServerPackets.SwitchTournamentServer, PacketWriter.WriteString(ip));

    private static byte[] WriteInt64(long value)
    {
        var result = new byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(result, value);
        return result;
    }

    private static byte[] WriteUInt16(ushort value)
    {
        var result = new byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(result, value);
        return result;
    }

    private static byte[] WriteFloat32(float value)
    {
        var result = new byte[4];
        BinaryPrimitives.WriteSingleLittleEndian(result, value);
        return result;
    }
}

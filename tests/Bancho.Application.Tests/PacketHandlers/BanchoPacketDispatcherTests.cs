using Bancho.Application.PacketHandlers;
using Bancho.Application.Sessions;
using Bancho.Domain;
using Bancho.Protocol;
using Bancho.Application.PacketHandlers.Core;
using Bancho.Domain.Users;
using Bancho.Protocol.Packets;

namespace Bancho.Application.Tests.PacketHandlers;

/// <summary>
/// Ported from app/state/__init__.py's packet_map ("all"/"restricted" split) + the dispatch loop
/// in app/api/domains/cho.py's bancho_handler (`for packet in BanchoPacketReader(...): await
/// packet.handle(player)` — unhandled packet types are skipped via their declared length).
/// </summary>
public class BanchoPacketDispatcherTests
{
    private sealed class FakeHandler(ClientPackets packetId, bool allowedWhenRestricted, Action<PlayerSession, BanchoPacketReader> onHandle) : IBanchoPacketHandler
    {
        public ClientPackets PacketId => packetId;
        public bool AllowedWhenRestricted => allowedWhenRestricted;

        public Task HandleAsync(PlayerSession player, BanchoPacketReader reader)
        {
            onHandle(player, reader);
            return Task.CompletedTask;
        }
    }

    private static byte[] PacketBytes(ClientPackets type, byte[] payload)
    {
        var header = new byte[7];
        BitConverterHeader(header, type, payload.Length);
        return [.. header, .. payload];
    }

    private static void BitConverterHeader(byte[] header, ClientPackets type, int length)
    {
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(header, (ushort)type);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(3), length);
    }

    [Fact]
    public async Task Dispatch_KnownPacket_InvokesHandlerWithReaderPositionedAfterHeader()
    {
        var called = false;
        var handler = new FakeHandler(ClientPackets.Ping, allowedWhenRestricted: true, (_, _) => called = true);
        var dispatcher = new BanchoPacketDispatcher([handler]);
        var session = new PlayerSession(1, "cmyui", "token", Privileges.Unrestricted, 0.0);

        await dispatcher.DispatchAsync(session, PacketBytes(ClientPackets.Ping, []));

        Assert.True(called);
    }

    [Fact]
    public async Task Dispatch_UnknownPacket_SkippedWithoutError()
    {
        var dispatcher = new BanchoPacketDispatcher([]);
        var session = new PlayerSession(1, "cmyui", "token", Privileges.Unrestricted, 0.0);

        await dispatcher.DispatchAsync(session, PacketBytes(ClientPackets.CantSpectate, [1, 2, 3, 4]));
        // no exception -> success
    }

    [Fact]
    public async Task Dispatch_MultiplePacketsInOneRequest_InvokesEachHandler()
    {
        var calls = new List<ClientPackets>();
        var pingHandler = new FakeHandler(ClientPackets.Ping, true, (_, _) => calls.Add(ClientPackets.Ping));
        var logoutHandler = new FakeHandler(ClientPackets.Logout, true, (_, r) => { r.ReadI32(); calls.Add(ClientPackets.Logout); });
        var dispatcher = new BanchoPacketDispatcher([pingHandler, logoutHandler]);
        var session = new PlayerSession(1, "cmyui", "token", Privileges.Unrestricted, 0.0);
        var body = PacketBytes(ClientPackets.Ping, []).Concat(PacketBytes(ClientPackets.Logout, PacketWriter.WriteInt32(0))).ToArray();

        await dispatcher.DispatchAsync(session, body);

        Assert.Equal([ClientPackets.Ping, ClientPackets.Logout], calls);
    }

    [Fact]
    public async Task Dispatch_RestrictedPlayer_OnlyInvokesRestrictedAllowedHandlers()
    {
        var pingCalled = false;
        var chatCalled = false;
        var pingHandler = new FakeHandler(ClientPackets.Ping, allowedWhenRestricted: true, (_, _) => pingCalled = true);
        var chatHandler = new FakeHandler(ClientPackets.SendPublicMessage, allowedWhenRestricted: false, (_, _) => chatCalled = true);
        var dispatcher = new BanchoPacketDispatcher([pingHandler, chatHandler]);
        var restrictedSession = new PlayerSession(1, "cmyui", "token", Privileges.Verified, 0.0); // restricted

        var body = PacketBytes(ClientPackets.Ping, []).Concat(PacketBytes(ClientPackets.SendPublicMessage, [1, 2])).ToArray();
        await dispatcher.DispatchAsync(restrictedSession, body);

        Assert.True(pingCalled);
        Assert.False(chatCalled);
    }
}

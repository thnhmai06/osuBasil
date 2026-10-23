using Basil.Protocol.Bancho.Wire.Packets;

namespace Basil.Protocol.Bancho.Tests.Wire.Packets;

/// <summary>Verifies the stream contract of <see cref="PacketWriter" />: seekable input required, and the stream is left open.</summary>
public class PacketWriterStreamContractTests
{
	private sealed class NonSeekableStream : MemoryStream
	{
		public override bool CanSeek => false;
	}

	[Fact]
	public void Constructor_NonSeekableStream_ThrowsArgumentException()
	{
		Assert.Throws<ArgumentException>(() => new PacketWriter(new NonSeekableStream()));
	}

	[Fact]
	public void WritePacket_LeavesTheStreamOpen()
	{
		var stream = new MemoryStream();
		var writer = new PacketWriter(stream);

		writer.Pong();

		Assert.True(stream.CanRead);
		Assert.True(stream.CanWrite);
		Assert.Equal(7, stream.Length);
	}
}
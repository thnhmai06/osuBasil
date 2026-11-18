using Basil.Protocol.Stable.Wire.Packets;

namespace Basil.Protocol.Stable.Tests.Wire.Packets;

public class PacketWriterStreamContractTests
{
	[Fact]
	public void NonSeekableStream_ThrowsArgumentException()
	{
		Assert.Throws<ArgumentException>(() => new PacketWriter(new NonSeekableStream()));
	}

	[Fact]
	public void WritePacket_LeavesStreamOpen()
	{
		var stream = new MemoryStream();
		var writer = new PacketWriter(stream);

		writer.Pong();

		Assert.True(stream.CanRead);
		Assert.True(stream.CanWrite);
		Assert.Equal(7, stream.Length);
	}

	private sealed class NonSeekableStream : MemoryStream
	{
		public override bool CanSeek => false;
	}
}
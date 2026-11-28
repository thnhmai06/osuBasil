using System.Text;

namespace Basil.Protocol.Game.Stable.Wire.Binary;

internal static class BinaryWriterExtensions
{
	extension(BinaryWriter writer)
	{
		public void WriteUleb128(uint value)
		{
			do
			{
				var b = (byte)(value & 0x7F);
				value >>= 7;

				if (value != 0)
					b |= 0x80;

				writer.Write(b);
			} while (value != 0);
		}

		public void WriteOsuString(string? value)
		{
			if (string.IsNullOrEmpty(value))
			{
				writer.Write((byte)0x00);
				return;
			}

			var encoded = Encoding.UTF8.GetBytes(value);

			writer.Write((byte)0x0B);
			writer.WriteUleb128((uint)encoded.Length);
			writer.Write(encoded);
		}

		public void WriteInt32List(IReadOnlyList<int> values)
		{
			writer.Write(checked((ushort)values.Count));
			foreach (var value in values)
				writer.Write(value);
		}
	}
}
using System.Text;

namespace Basil.Protocol.Bancho.Wire.Binary;

/// <summary>Provides low-level binary encoding primitives for the Bancho protocol.</summary>
internal static class BinaryWriterExtensions
{
	extension(BinaryWriter writer)
	{
		/// <summary>Writes an unsigned integer as a ULEB128 variable-length byte sequence.</summary>
		/// <param name="value">The value to encode; the larger the value, the more bytes are written (up to five).</param>
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

		/// <summary>Writes a string in the osu! wire format.</summary>
		/// <param name="value">
		///     The string to write. A <c>null</c> or empty string is written as a single
		///     <c>0x00</c> existence byte (the equivalent of the reader's null marker); any other
		///     value is written as <c>0x0B</c>, a ULEB128 byte length, then the UTF-8 bytes.
		/// </param>
		/// <remarks>Named to avoid colliding with <see cref="BinaryWriter.Write(string)" />, which uses .NET's own wire format.</remarks>
		public void WriteOsuString(string value)
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

		/// <summary>Writes a list of 32-bit integers with a 16-bit unsigned count prefix.</summary>
		/// <param name="values">The values to write, preceded by their count as a <see cref="ushort" />.</param>
		/// <exception cref="OverflowException">
		///     <paramref name="values" /> contains more than <c>65535</c> entries, so its count
		///     cannot be represented by the 16-bit prefix.
		/// </exception>
		public void WriteI32ListI16L(IReadOnlyList<int> values)
		{
			writer.Write(checked((ushort)values.Count));
			foreach (var value in values)
				writer.Write(value);
		}
	}
}
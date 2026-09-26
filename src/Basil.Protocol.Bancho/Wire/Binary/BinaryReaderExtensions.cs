using System.Text;

namespace Basil.Protocol.Bancho.Wire.Binary;

/// <summary>Provides low-level binary decoding primitives for the Bancho protocol.</summary>
/// <remarks>
///     All multi-byte integers are read in little-endian byte order, matching the osu! protocol.
/// </remarks>
internal static class BinaryReaderExtensions
{
	extension(BinaryReader reader)
	{
		#region Raw

		/// <summary>Reads exactly <paramref name="length" /> bytes from the stream.</summary>
		/// <param name="length">The number of bytes to read.</param>
		/// <returns>A new byte array containing the next <paramref name="length" /> bytes.</returns>
		private byte[] ReadRaw(int length)
		{
			var value = reader.ReadBytes(length);
			return value.Length == length ? value : throw new EndOfStreamException();
		}

		/// <summary>Advances past <paramref name="length" /> bytes, for skipping unhandled packet payloads.</summary>
		/// <param name="length">The number of bytes to skip.</param>
		private void SkipRaw(int length)
		{
			Span<byte> buffer = stackalloc byte[Math.Min(length, 4096)];

			while (length > 0)
			{
				var count = reader.BaseStream.Read(buffer[..Math.Min(length, buffer.Length)]);
				if (count == 0) throw new EndOfStreamException();
				length -= count;
			}
		}

		#endregion

		#region Complex

		/// <summary>Reads a list of 32-bit integers prefixed by a 16-bit unsigned count.</summary>
		/// <returns>The list of values read.</returns>
		public IReadOnlyList<int> ReadI32ListI16L()
		{
			var length = reader.ReadUInt16();
			var values = new int[length];

			for (var i = 0; i < length; i++)
				values[i] = reader.ReadInt32();

			return values;
		}

		/// <summary>Reads a list of 32-bit integers prefixed by a 32-bit unsigned count.</summary>
		/// <returns>The list of values read.</returns>
		public IReadOnlyList<int> ReadI32ListI32L()
		{
			var length = reader.ReadUInt32();
			var values = new int[length];

			for (var i = 0; i < length; i++)
				values[i] = reader.ReadInt32();

			return values;
		}

		/// <summary>
		///     Reads an osu!-format string: an existence byte, a ULEB128 length, then the UTF-8
		///     bytes.
		/// </summary>
		/// <returns>
		///     The string value read, or an empty string when the existence byte is not
		///     <c>0x0B</c> (the null marker).
		/// </returns>
		/// <exception cref="EndOfStreamException">
		///     The stream ends before the existence byte, the length, or the string bytes can be
		///     read.
		/// </exception>
		public string ReadOsuString()
		{
			var exists = reader.ReadSByte() == 0x0B;
			if (!exists) return "";

			var length = 0;
			var shift = 0;

			while (true)
			{
				var b = reader.ReadSByte();
				length |= (b & 0x7F) << shift;
				if ((b & 0x80) == 0) break;
				shift += 7;
			}

			var bytes = reader.ReadRaw(length);
			return Encoding.UTF8.GetString(bytes);
		}

		#endregion
	}
}
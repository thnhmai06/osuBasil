using System.Text;

namespace Basil.Protocol.Bancho.Wire.Binary;

/// <summary>Provides low-level binary decoding primitives for the Bancho protocol.</summary>
/// <remarks>
///     All multi-byte integers are read in little-endian byte order, matching the osu! protocol.
/// </remarks>
internal static class BinaryReaderExtensions
{
	/// <summary>The largest string the reader decodes, in UTF-8 bytes.</summary>
	public const int MaxStringBytes = 1_048_576;

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

		#endregion

		#region Complex

		/// <summary>Reads a list of 32-bit integers prefixed by a 16-bit unsigned count.</summary>
		/// <returns>The list of values read, empty when the count is zero.</returns>
		public IReadOnlyList<int> ReadInt32List()
		{
			var length = reader.ReadUInt16();
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
		/// <exception cref="InvalidDataException">
		///     The length needs more than five bytes, is negative, or is above
		///     <see cref="MaxStringBytes" />.
		/// </exception>
		public string ReadOsuString()
		{
			const int maxLengthBytes = 5;

			var exists = reader.ReadSByte() == 0x0B;
			if (!exists) return "";

			var length = 0;
			for (var index = 0;; index++)
			{
				if (index == maxLengthBytes)
					throw new InvalidDataException("The osu! string length needs more than five bytes.");

				var b = reader.ReadSByte();
				length |= (b & 0x7F) << (index * 7);
				if ((b & 0x80) == 0) break;
			}

			if (length is < 0 or > MaxStringBytes)
				throw new InvalidDataException("The osu! string length is out of range.");

			var bytes = reader.ReadRaw(length);
			return Encoding.UTF8.GetString(bytes);
		}

		#endregion
	}
}
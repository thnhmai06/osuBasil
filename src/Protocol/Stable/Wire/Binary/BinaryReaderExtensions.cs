using System.Text;

namespace Basil.Protocol.Stable.Wire.Binary;

internal static class BinaryReaderExtensions
{
	public const int MaxStringBytes = 1_048_576;

	extension(BinaryReader reader)
	{
		#region Raw

		private byte[] ReadRaw(int length)
		{
			var value = reader.ReadBytes(length);
			return value.Length == length ? value : throw new EndOfStreamException();
		}

		#endregion

		#region Complex

		public IReadOnlyList<int> ReadInt32List()
		{
			var length = reader.ReadUInt16();
			var values = new int[length];

			for (var i = 0; i < length; i++)
				values[i] = reader.ReadInt32();

			return values;
		}

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
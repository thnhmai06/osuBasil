using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Basil.Domain.Client;
using Basil.Domain.Mechanics;
using Basil.Domain.Scores;

namespace Basil.Infrastructure.Scores.Storage;

/// <summary>The <c>.osr</c> replay file: its header around the LZMA frames the osu! client submits.</summary>
internal static class ReplayFile
{
	private const byte StringMarker = 0x0B;

	/// <summary>Writes the complete <c>.osr</c> file of a score around the frames the client submitted.</summary>
	/// <param name="output">Where the file is written; it must be seekable.</param>
	/// <param name="score">The score whose result the header records.</param>
	/// <param name="playerName">The player's name; empty when the player is unknown.</param>
	/// <param name="clientVersion">The osu! client version that played the score.</param>
	/// <param name="frames">
	///     The LZMA-compressed frames exactly as the client submitted them, from the current position to the end.
	/// </param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	public static async Task WriteAsync(Stream output, ScoreRecord score, string playerName, OsuVersion clientVersion,
		Stream frames, CancellationToken cancellationToken = default)
	{
		var data = score.Value;
		var (counts, totalScore, maxCombo, _, isFullCombo) = data.LastState;
		var beatmapHash = data.BeatmapHash?.ToString() ?? string.Empty;
		var mods = (int)data.Mods;

		var checksum = Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(string.Create(
			CultureInfo.InvariantCulture,
			$"{counts.Num100 + counts.Num300}p{counts.Num50}o{counts.NumGeki}o{counts.NumKatu}t{counts.NumMiss}" +
			$"a{beatmapHash}r{maxCombo}e{isFullCombo}y{playerName}o{totalScore}u0{mods}True"))));

		using (var writer = new BinaryWriter(output, Encoding.UTF8, true))
		{
			writer.Write((byte)data.Mode);
			writer.Write(clientVersion.Date.Year * 10000 + clientVersion.Date.Month * 100 + clientVersion.Date.Day);
			WriteString(writer, beatmapHash);
			WriteString(writer, playerName);
			WriteString(writer, checksum);
			writer.Write(Count(counts.Num300));
			writer.Write(Count(counts.Num100));
			writer.Write(Count(counts.Num50));
			writer.Write(Count(counts.NumGeki));
			writer.Write(Count(counts.NumKatu));
			writer.Write(Count(counts.NumMiss));
			writer.Write(totalScore);
			writer.Write(maxCombo);
			writer.Write(isFullCombo);
			writer.Write(mods);
			WriteString(writer, string.Empty); // the life bar graph, which the server does not keep
			writer.Write(data.Timestamp.UtcTicks);
			writer.Write(0); // the frames' length, written once they are copied
		}

		var start = output.Position;
		await frames.CopyToAsync(output, cancellationToken);
		var end = output.Position;
		output.Position = start - sizeof(int);
		using (var writer = new BinaryWriter(output, Encoding.UTF8, true))
		{
			writer.Write(checked((int)(end - start)));
		}

		output.Position = end;
		using (var writer = new BinaryWriter(output, Encoding.UTF8, true))
		{
			writer.Write((long)score.Id);
			if ((data.Mods & GameMods.Target) != GameMods.NoMod) writer.Write(0.0);
		}
	}

	/// <summary>Finds the LZMA frames inside a complete <c>.osr</c> file, reading only its header.</summary>
	/// <param name="file">The file, as a seekable stream positioned anywhere; its position afterwards is unspecified.</param>
	/// <param name="offset">Where the frames start.</param>
	/// <param name="length">How many bytes the frames take.</param>
	/// <returns><see langword="true" /> when the file is a well-formed replay; otherwise, <see langword="false" />.</returns>
	public static bool TryFindFrames(Stream file, out long offset, out long length)
	{
		offset = length = 0;
		try
		{
			file.Position = 0;
			using var reader = new BinaryReader(file, Encoding.UTF8, true);
			reader.ReadByte(); // mode
			reader.ReadInt32(); // client version
			ReadString(reader); // beatmap hash
			ReadString(reader); // player name
			ReadString(reader); // checksum
			file.Position += 6 * sizeof(short) + sizeof(int) + sizeof(short) + sizeof(byte) + sizeof(int);
			ReadString(reader); // life bar graph
			reader.ReadInt64(); // timestamp
			var count = reader.ReadInt32();
			if (count < 0 || file.Position + count > file.Length) return false;

			offset = file.Position;
			length = count;
			return true;
		}
		catch (Exception exception) when (exception is EndOfStreamException or InvalidDataException
			                                  or ArgumentException)
		{
			return false;
		}
	}

	/// <summary>A hit count as the file stores it: 16 bits, so a count beyond them is kept at the largest.</summary>
	private static ushort Count(int count)
	{
		return (ushort)Math.Min(count, ushort.MaxValue);
	}

	private static void WriteString(BinaryWriter writer, string value)
	{
		if (value.Length == 0)
		{
			writer.Write((byte)0);
			return;
		}

		var bytes = Encoding.UTF8.GetBytes(value);
		writer.Write(StringMarker);
		for (var rest = (uint)bytes.Length;;)
		{
			var part = (byte)(rest & 0x7F);
			rest >>= 7;
			if (rest == 0)
			{
				writer.Write(part);
				break;
			}

			writer.Write((byte)(part | 0x80));
		}

		writer.Write(bytes);
	}

	private static void ReadString(BinaryReader reader)
	{
		var marker = reader.ReadByte();
		if (marker == 0) return;
		if (marker != StringMarker) throw new InvalidDataException("Not a string.");

		var size = 0;
		for (var shift = 0;; shift += 7)
		{
			if (shift > 28) throw new InvalidDataException("String length is too long.");
			var part = reader.ReadByte();
			size |= (part & 0x7F) << shift;
			if ((part & 0x80) == 0) break;
		}

		if (size < 0 || reader.BaseStream.Position + size > reader.BaseStream.Length)
			throw new EndOfStreamException();
		reader.BaseStream.Position += size;
	}
}

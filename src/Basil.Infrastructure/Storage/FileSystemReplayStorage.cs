using System.Security.Cryptography;
using System.Text;
using Basil.Application.Abstractions.Scores;
using Basil.Application.Configurations;
using Basil.Domain.Login;
using Basil.Domain.Scores;
using Microsoft.Extensions.Options;
using BinaryWriter = Basil.Protocol.Binary.BinaryWriter;

namespace Basil.Infrastructure.Storage;

/// <inheritdoc cref="IReplayStorage" />
/// <remarks>
///     Stores each replay as a single complete <c>.osr</c> file named <c>{scoreId}.osr</c> in
///     <see cref="StorageOptions.ReplaysPath" />, building the <c>.osr</c> header around the raw
///     LZMA replay bytes on writing. A read of a score with no stored replay returns null rather than
///     throwing.
/// </remarks>
public sealed class FileSystemReplayStorage(IOptions<StorageOptions> options) : IReplayStorage
{
	/// <inheritdoc />
	/// <remarks>Creates the replays folder when it does not yet exist, then writes the file.</remarks>
	public async Task WriteAsync(
		long scoreId, Submission score, string playerName, OsuVersion osuVersion,
		byte[] replayData, CancellationToken cancellationToken = default)
	{
		Directory.CreateDirectory(options.Value.ReplaysPath);
		await File.WriteAllBytesAsync(PathFor(scoreId), BuildOsr(scoreId, score, playerName, osuVersion, replayData),
			cancellationToken);
	}

	/// <summary>
	///     Builds a complete <c>.osr</c> file: the header written around the client's raw LZMA replay
	///     bytes.
	/// </summary>
	/// <param name="scoreId">The id of the score the replay belongs to.</param>
	/// <param name="score">The submitted score whose stats the header records.</param>
	/// <param name="playerName">The name of the player who submitted the score.</param>
	/// <param name="osuVersion">The game version the replay belongs to.</param>
	/// <param name="replayData">The raw LZMA replay bytes from the client's submission.</param>
	/// <returns>The complete <c>.osr</c> file bytes.</returns>
	private static byte[] BuildOsr(
		long scoreId, Submission score, string playerName, OsuVersion osuVersion, byte[] replayData)
	{
		var replayMd5 = Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(
			$"{score.HitCounts.x100 + score.HitCounts.x300}p{score.HitCounts.x50}o{score.HitCounts.xGeki}" +
			$"o{score.HitCounts.xKatu}t{score.HitCounts.xMiss}a{score.BeatmapMd5}r{score.MaxCombo}" +
			$"e{score.IsFullCombo}y{playerName}o{score.Score}u0{(int)score.Mods}True")));

		var result = new List<byte>();
		result.AddRange(BinaryWriter.WriteByte((byte)score.Mode));
		result.AddRange(BinaryWriter.WriteInt32(int.Parse(osuVersion.Date.ToString("yyyyMMdd"))));
		result.AddRange(BinaryWriter.WriteString(score.BeatmapMd5));
		result.AddRange(BinaryWriter.WriteString(playerName));
		result.AddRange(BinaryWriter.WriteString(replayMd5));
		result.AddRange(BinaryWriter.WriteInt16((short)score.HitCounts.x300));
		result.AddRange(BinaryWriter.WriteInt16((short)score.HitCounts.x100));
		result.AddRange(BinaryWriter.WriteInt16((short)score.HitCounts.x50));
		result.AddRange(BinaryWriter.WriteInt16((short)score.HitCounts.xGeki));
		result.AddRange(BinaryWriter.WriteInt16((short)score.HitCounts.xKatu));
		result.AddRange(BinaryWriter.WriteInt16((short)score.HitCounts.xMiss));
		result.AddRange(BinaryWriter.WriteInt32((int)score.Score));
		result.AddRange(BinaryWriter.WriteInt16((short)score.MaxCombo));
		result.AddRange(BinaryWriter.WriteByte(score.IsFullCombo ? (byte)1 : (byte)0));
		result.AddRange(BinaryWriter.WriteInt32((int)score.Mods));
		result.AddRange(BinaryWriter.WriteString(string.Empty)); // Life bar graph
		result.AddRange(BinaryWriter.WriteInt64(score.ServerTime.Ticks));
		result.AddRange(BinaryWriter.WriteInt32(replayData.Length));
		result.AddRange(replayData);
		result.AddRange(BinaryWriter.WriteInt64(scoreId));
		if ((score.Mods & Mods.Target) != 0) result.AddRange(BinaryWriter.WriteDouble(0));

		return [.. result];
	}

	/// <inheritdoc />
	public async Task<byte[]?> ReadAsync(long scoreId, CancellationToken cancellationToken = default)
	{
		var path = PathFor(scoreId);
		return File.Exists(path) ? await File.ReadAllBytesAsync(path, cancellationToken) : null;
	}

	/// <summary>Builds the absolute path of a score's replay file.</summary>
	/// <param name="scoreId">The id of the score.</param>
	/// <returns>The absolute path of the <c>.osr</c> file for the score.</returns>
	private string PathFor(long scoreId)
	{
		return Path.Combine(options.Value.ReplaysPath, $"{scoreId}.osr");
	}
}
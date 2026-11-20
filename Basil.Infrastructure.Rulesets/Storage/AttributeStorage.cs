using System.Globalization;
using Basil.Domain.Game;
using Basil.Shared.Structures.Cryptography;

namespace Basil.Infrastructure.Rulesets.Storage;

/// <summary>Keeps the calculated difficulty attributes of beatmap files, by checksum, mode and mods.</summary>
internal sealed class AttributeStorage(RulesetStore rulesets)
{
	public async Task<string?> ReadAsync(Md5Hash checksum, GameMode mode, GameMods mask, CancellationToken ct)
	{
		try
		{
			return await File.ReadAllTextAsync(PathOf(checksum, mode, mask), ct);
		}
		catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
		{
			return null;
		}
	}

	public async Task WriteAsync(Md5Hash checksum, GameMode mode, GameMods mask, string content)
	{
		var path = PathOf(checksum, mode, mask);
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);
		var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
		try
		{
			await File.WriteAllTextAsync(temporary, content);
			File.Move(temporary, path, true);
		}
		finally
		{
			if (File.Exists(temporary)) File.Delete(temporary);
		}
	}

	private string PathOf(Md5Hash checksum, GameMode mode, GameMods mask)
	{
		return Path.Combine(rulesets.Root, "Attributes", checksum.HashValue,
			((int)mode).ToString(CultureInfo.InvariantCulture), $"{(int)mask}.json");
	}
}
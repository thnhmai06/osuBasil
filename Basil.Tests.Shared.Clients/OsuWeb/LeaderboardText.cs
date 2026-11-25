using System.Globalization;
using Basil.Protocol.Game.Stable.Models.Web;
using Basil.Protocol.Game.Stable.Wire.Web;

namespace Basil.Tests.Shared.Clients.OsuWeb;

/// <summary>Reads the leaderboard text of the getscores endpoint back into the model the server writes it from.</summary>
public static class LeaderboardText
{
	private const int HeaderLines = 5;

	private const int RowFields = 16;

	/// <summary>Reads a leaderboard.</summary>
	/// <param name="text">The body of the getscores answer.</param>
	/// <returns>The leaderboard, or <see langword="null"/> when the server does not know the beatmap or the text is not a leaderboard.</returns>
	public static LeaderboardReply? Parse(string text)
	{
		if (text == OsuWebWriter.LeaderboardNotFound) return null;

		var lines = text.Split('\n');
		var head = lines[0].Split('|');
		if (lines.Length < HeaderLines || head.Length < 6 || !int.TryParse(head[0], out var status) ||
		    !int.TryParse(head[2], out var beatmapId) || !int.TryParse(head[3], out var setId))
			return null;

		var rows = new List<LeaderboardRow>();
		foreach (var line in lines.Skip(HeaderLines).Where(line => line.Length > 0))
		{
			var f = line.Split('|');
			if (f.Length < RowFields) return null;

			rows.Add(new LeaderboardRow(Long(f[0]), f[1], Long(f[2]), Int(f[3]), Int(f[4]), Int(f[5]), Int(f[6]),
				Int(f[7]), Int(f[8]), Int(f[9]), f[10] == "1", Int(f[11]), Int(f[12]), Int(f[13]), Long(f[14])));
		}

		return new LeaderboardReply(status, beatmapId, setId, lines[2], rows);
	}

	private static int Int(string value)
	{
		return int.Parse(value, CultureInfo.InvariantCulture);
	}

	private static long Long(string value)
	{
		return long.Parse(value, CultureInfo.InvariantCulture);
	}
}

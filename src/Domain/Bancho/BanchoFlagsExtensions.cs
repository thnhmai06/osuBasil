namespace Basil.Domain.Bancho;

/// <summary>Reads <see cref="BanchoFlags" /> as the names of the signs it carries.</summary>
public static class BanchoFlagsExtensions
{
	/// <param name="flags">The flags to read.</param>
	extension(BanchoFlags flags)
	{
		/// <summary>Lists the names of the signs the flags carry.</summary>
		/// <returns>
		///     The name of each set sign, in ascending bit order; empty for a clean client. Bits that are not a
		///     defined sign are ignored.
		/// </returns>
		public IReadOnlyList<string> SignNames()
		{
			return
			[
				.. Enum.GetValues<BanchoFlags>()
					.Where(sign => sign != BanchoFlags.Clean && flags.HasFlag(sign))
					.Select(sign => sign.ToString())
			];
		}
	}
}

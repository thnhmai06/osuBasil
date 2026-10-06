using Basil.Domain.Utilities;

namespace Basil.Domain.Client;

/// <summary>
///     Represents the network adapter data an osu! client reports: the raw adapter string and the
///     MD5 checksum the client computes over it.
/// </summary>
/// <remarks>
///     Wine clients (Proton, PlayOnLinux) report the literal sentinel <c>runningunderwine</c>
///     instead of a real adapter list, because their adapter set does not reflect the machine's
///     hardware. Other adapter strings are dot-separated, end with a dot, and contain no empty
///     chunks. Comparisons ignore the case of the checksum.
/// </remarks>
public readonly record struct NetworkAdapters
{
	private const string WineAdapterSentinel = "runningunderwine";

	/// <summary>
	///     Creates an adapter value from its two wire components.
	/// </summary>
	/// <param name="adapters">
	///     The raw adapter string: either <c>runningunderwine</c> or a dot-separated,
	///     dot-terminated list of adapter names.
	/// </param>
	/// <param name="hash">The MD5 checksum of the adapter string.</param>
	/// <exception cref="FormatException">
	///     <paramref name="adapters" /> is empty or malformed, or <paramref name="hash" /> is not a
	///     32-character hexadecimal string.
	/// </exception>
	public NetworkAdapters(string adapters, Md5 hash)
	{
		if (string.IsNullOrEmpty(adapters))
			throw new FormatException("Adapter string cannot be empty.");

		if (!IsValidAdaptersString(adapters))
			throw new FormatException("Adapter string is invalid.");

		Adapters = adapters;
		Hash = hash;
	}

	/// <summary>The raw adapter string reported by the client, without modification.</summary>
	public string Adapters { get; }

	/// <summary>
	///     The MD5 checksum of the adapter string, normalized to lowercase.
	/// </summary>
	public Md5 Hash { get; }

	/// <summary>
	///     Indicates whether the client reported that it is running under Wine.
	/// </summary>
	/// <remarks>
	///     <see langword="true" /> when <see cref="Adapters" /> equals the
	///     <c>runningunderwine</c> sentinel, ignoring case.
	/// </remarks>
	public bool IsRunningUnderWine => Adapters.Equals(WineAdapterSentinel, StringComparison.OrdinalIgnoreCase);

	/// <summary>
	///     Determines whether this adapter value has the same MD5 checksum as
	///     <paramref name="other" />.
	/// </summary>
	/// <remarks>
	///     Only <see cref="Hash" /> is compared, ignoring case; the adapter strings themselves are
	///     not compared.
	/// </remarks>
	/// <param name="other">The value to compare against.</param>
	/// <returns>
	///     <see langword="true" /> if the checksums match ignoring case; otherwise,
	///     <see langword="false" />.
	/// </returns>
	public bool Equals(NetworkAdapters other)
	{
		return Hash == other.Hash;
	}

	/// <summary>
	///     Returns a hash code based on the MD5 checksum, ignoring case.
	/// </summary>
	/// <returns>The hash code of <see cref="Hash" /> under ordinal, case-insensitive comparison.</returns>
	public override int GetHashCode()
	{
		return StringComparer.OrdinalIgnoreCase.GetHashCode(Hash);
	}

	private static bool IsValidAdaptersString(string value)
	{
		if (value.Equals(WineAdapterSentinel, StringComparison.OrdinalIgnoreCase))
			return true;

		return value.EndsWith('.')
		       && value[..^1]
			       .Split('.', StringSplitOptions.RemoveEmptyEntries)
			       .All(a => !a.Equals(WineAdapterSentinel, StringComparison.OrdinalIgnoreCase));
	}
}
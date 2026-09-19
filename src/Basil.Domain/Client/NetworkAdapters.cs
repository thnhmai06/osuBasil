using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

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
public readonly partial record struct NetworkAdapters : IParsable<NetworkAdapters>
{
	private const string WineAdapterSentinel = "runningunderwine";

	/// <summary>
	///     Creates an adapter value from its two wire components.
	/// </summary>
	/// <param name="adapters">
	///     The raw adapter string: either <c>runningunderwine</c> or a dot-separated,
	///     dot-terminated list of adapter names.
	/// </param>
	/// <param name="md5">The MD5 checksum of the adapter string.</param>
	/// <exception cref="FormatException">
	///     <paramref name="adapters" /> is empty or malformed, or <paramref name="md5" /> is not a
	///     32-character hexadecimal string.
	/// </exception>
	public NetworkAdapters(string adapters, string md5)
	{
		if (string.IsNullOrEmpty(adapters))
			throw new FormatException("Adapter string cannot be empty.");

		if (!IsValidAdaptersString(adapters))
			throw new FormatException("Adapter string is invalid.");

		if (!Md5Pattern().IsMatch(md5))
			throw new FormatException("Adapter MD5 is invalid.");

		Adapters = adapters;
		Md5 = md5.ToLowerInvariant();
	}

	/// <summary>The raw adapter string reported by the client, without modification.</summary>
	public string Adapters { get; }

	/// <summary>
	///     The MD5 checksum of the adapter string, normalized to lowercase.
	/// </summary>
	public string Md5 { get; }

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
	///     Only <see cref="Md5" /> is compared, ignoring case; the adapter strings themselves are
	///     not compared.
	/// </remarks>
	/// <param name="other">The value to compare against.</param>
	/// <returns>
	///     <see langword="true" /> if the checksums match ignoring case; otherwise,
	///     <see langword="false" />.
	/// </returns>
	public bool Equals(NetworkAdapters other)
	{
		return string.Equals(Md5, other.Md5, StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>
	///     Parses an adapter value of the form <c>adapters:md5</c>.
	/// </summary>
	/// <param name="s">The wire value to parse.</param>
	/// <param name="provider">Ignored.</param>
	/// <returns>The parsed adapter value.</returns>
	/// <exception cref="FormatException">
	///     <paramref name="s" /> does not contain exactly two colon-separated components, or a
	///     component is invalid.
	/// </exception>
	public static NetworkAdapters Parse(string s, IFormatProvider? provider = null)
	{
		var parts = s.Split(':', 2);

		return parts.Length == 2
			? new NetworkAdapters(parts[0], parts[1])
			: throw new FormatException("Network adapters value must contain 2 components.");
	}

	/// <summary>
	///     Attempts to parse an adapter value.
	/// </summary>
	/// <param name="s">The wire value to parse, or <see langword="null" />.</param>
	/// <param name="provider">Ignored.</param>
	/// <param name="result">
	///     When this method returns <see langword="true" />, the parsed value; otherwise, the
	///     default value.
	/// </param>
	/// <returns>
	///     <see langword="true" /> if <paramref name="s" /> was parsed successfully; otherwise,
	///     <see langword="false" />.
	/// </returns>
	public static bool TryParse(
		[NotNullWhen(true)] string? s,
		IFormatProvider? provider,
		out NetworkAdapters result)
	{
		if (s is null)
		{
			result = default;
			return false;
		}

		try
		{
			result = Parse(s, provider);
			return true;
		}
		catch (FormatException)
		{
			result = default;
			return false;
		}
	}

	/// <summary>
	///     Returns the adapter value in <c>adapters:md5</c> wire form.
	/// </summary>
	/// <returns>The wire representation of this adapter value.</returns>
	public override string ToString()
	{
		return $"{Adapters}:{Md5}";
	}

	/// <summary>
	///     Returns a hash code based on the MD5 checksum, ignoring case.
	/// </summary>
	/// <returns>The hash code of <see cref="Md5" /> under ordinal, case-insensitive comparison.</returns>
	public override int GetHashCode()
	{
		return StringComparer.OrdinalIgnoreCase.GetHashCode(Md5);
	}

	[GeneratedRegex("^[a-fA-F0-9]{32}$")]
	private static partial Regex Md5Pattern();

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
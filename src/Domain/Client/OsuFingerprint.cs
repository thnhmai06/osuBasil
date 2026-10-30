using Basil.Domain.Common;

namespace Basil.Domain.Client;

/// <summary>
///     Represents the client fingerprint captured from an osu! client at login.
/// </summary>
/// <remarks>
///     The fingerprint is the osu! client hash: five components identifying the
///     installation (and, after re-installation, the machine) a player is using.
/// </remarks>
public readonly record struct OsuFingerprint(
	Md5 PathHash,
	Md5 UninstallHash,
	Md5 DiskSignatureHash,
	NetworkAdapters NetworkAdapters);

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

	/// <summary>The raw adapter string reported by the client, without modification.</summary>
	/// <exception cref="ArgumentNullException">The value is <see langword="null" />.</exception>
	public required string Adapters
	{
		get;
		init
		{
			ArgumentNullException.ThrowIfNull(value);

			if (!IsValidAdaptersString(value))
				throw new FormatException("Adapter string is invalid.");

			if (string.IsNullOrWhiteSpace(value))
				throw new FormatException("Adapter string cannot be empty.");

			field = value;
			return;

			static bool IsValidAdaptersString(string value)
			{
				if (value.HasNul())
					return false;

				if (value.Equals(WineAdapterSentinel, StringComparison.OrdinalIgnoreCase))
					return true;

				return value.EndsWith('.')
				       && value[..^1]
					       .Split('.')
					       .All(a => a.Length > 0 &&
					                 !a.Equals(WineAdapterSentinel, StringComparison.OrdinalIgnoreCase));
			}
		}
	}

	/// <summary>
	///     The MD5 checksum of the adapter string, normalized to lowercase.
	/// </summary>
	public required Md5 Hash { get; init; }

	/// <summary>
	///     Indicates whether the client reported that it is running under Wine.
	/// </summary>
	/// <remarks>
	///     <see langword="true" /> when <see cref="Adapters" /> equals the
	///     <c>runningunderwine</c> sentinel, ignoring case; <see langword="false" /> when there is no
	///     adapter string at all.
	/// </remarks>
	public bool IsRunningUnderWine => string.Equals(Adapters, WineAdapterSentinel, StringComparison.OrdinalIgnoreCase);

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
	///     Returns a hash code based on the MD5 checksum.
	/// </summary>
	/// <returns>The hash code of <see cref="Hash" />.</returns>
	public override int GetHashCode()
	{
		return StringComparer.OrdinalIgnoreCase.GetHashCode(Hash);
	}
}
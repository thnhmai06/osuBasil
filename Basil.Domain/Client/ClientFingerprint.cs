using Basil.Shared.Structures.Cryptography;
using Basil.Shared.Utilities;

namespace Basil.Domain.Client;

/// <summary>The hashes an osu! client reports to identify its installation and machine.</summary>
/// <param name="PathHash">The hash of the client's install path.</param>
/// <param name="UninstallHash">The hash of the client's uninstall identifier.</param>
/// <param name="DiskSignatureHash">The hash of the machine's disk signature.</param>
/// <param name="NetworkAdapters">The network adapters the client reported.</param>
public readonly record struct ClientFingerprint(
	Md5Hash PathHash,
	Md5Hash UninstallHash,
	Md5Hash DiskSignatureHash,
	NetworkAdapters NetworkAdapters);

/// <summary>The network adapters an osu! client reported, with the hash it reported for them; two values are equal when their hashes are equal.</summary>
public readonly record struct NetworkAdapters
{
	private const string WineAdapterSentinel = "runningunderwine";

	/// <summary>The adapters text the client reported: either the marker of a client running under Wine, or a list of adapter entries each ending with a dot.</summary>
	/// <exception cref="FormatException">The text is empty, holds a NUL character or is not in one of those forms.</exception>
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

	/// <summary>The hash the client reported for the adapters.</summary>
	public required Md5Hash Hash { get; init; }

	/// <summary>Whether the client reported that it runs under Wine.</summary>
	public bool IsRunningUnderWine => string.Equals(Adapters, WineAdapterSentinel, StringComparison.OrdinalIgnoreCase);

	public bool Equals(NetworkAdapters other)
	{
		return Hash == other.Hash;
	}

	public override int GetHashCode()
	{
		return StringComparer.OrdinalIgnoreCase.GetHashCode(Hash);
	}
}
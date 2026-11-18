using Basil.Shared.Structures.Cryptography;
using Basil.Shared.Utilities;

namespace Basil.Domain.Game.Client;

public readonly record struct OsuFingerprint(
	Md5Hash PathHash,
	Md5Hash UninstallHash,
	Md5Hash DiskSignatureHash,
	NetworkAdapters NetworkAdapters);

public readonly record struct NetworkAdapters
{
	private const string WineAdapterSentinel = "runningunderwine";

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

	public required Md5Hash Hash { get; init; }

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
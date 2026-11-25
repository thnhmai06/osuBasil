using System.Globalization;
using System.Numerics;
using System.Text.Json;
using Basil.Domain.Users;

namespace Basil.Host.Cli.Commands;

/// <summary>The names the console writes and reads for a set of flags, such as <c>player,supporter</c>.</summary>
public static class FlagNames
{
	public const string None = "none";

	/// <summary>Gets the name and the bit of every single flag of the type, in the spelling the HTTP API uses.</summary>
	public static IReadOnlyList<(string Name, ulong Bits)> Of(Type type)
	{
		return
		[
			.. Enum.GetValues(type).Cast<object>()
				.Select(flag => (Flag: flag, Bits: Convert.ToUInt64(flag, CultureInfo.InvariantCulture)))
				.Where(flag => BitOperations.PopCount(flag.Bits) == 1)
				.Select(flag => (JsonNamingPolicy.CamelCase.ConvertName(flag.Flag.ToString()!), flag.Bits))
		];
	}

	/// <summary>Reads names joined by commas into the flags they name; <paramref name="unknown"/> is the first word that names none.</summary>
	public static ulong Parse(Type type, string text, out string? unknown)
	{
		var known = Of(type);
		ulong bits = 0;
		unknown = null;
		foreach (var part in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
		{
			if (part.Equals(None, StringComparison.OrdinalIgnoreCase)) continue;

			var match = known.FirstOrDefault(flag => flag.Name.Equals(part, StringComparison.OrdinalIgnoreCase));
			if (match.Name is null)
			{
				unknown = part;
				return 0;
			}

			bits |= match.Bits;
		}

		return bits;
	}

	/// <summary>
	///     Rewrites <c>privilege=player,supporter</c> in a user search as the number the search takes, so a person types
	///     names while the search itself, shared with the HTTP API, stays numeric.
	/// </summary>
	public static string UserSearch(string query)
	{
		return string.Join(' ', query.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(word =>
		{
			const string key = "privilege=";
			if (!word.StartsWith(key, StringComparison.OrdinalIgnoreCase) || ulong.TryParse(word[key.Length..], out _))
				return word;

			var bits = Parse(typeof(UserPrivileges), word[key.Length..], out var unknown);
			return unknown is null
				? $"{key}{bits.ToString(CultureInfo.InvariantCulture)}"
				: throw CommandException.Invalid(
					$"privilege: '{unknown}' is not a name; use {string.Join(", ", Of(typeof(UserPrivileges)).Select(flag => flag.Name))}.");
		}));
	}

	/// <summary>Writes the flags that are set as names joined by commas, or <c>none</c>.</summary>
	public static string Join(Type type, ulong value)
	{
		var names = Of(type).Where(flag => (value & flag.Bits) != 0).Select(flag => flag.Name).ToArray();
		return names.Length == 0 ? None : string.Join(',', names);
	}
}

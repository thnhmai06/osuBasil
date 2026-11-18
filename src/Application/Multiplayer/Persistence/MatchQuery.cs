using System.Diagnostics.CodeAnalysis;
using Basil.Application.Shared.Paging;
using Basil.Domain.Users;

namespace Basil.Application.Multiplayer.Persistence;

public sealed record MatchQuery(bool? Ended = null, bool? Private = null, int? VisibleTo = null)
	: IParsable<MatchQuery>
{
	public static MatchQuery Parse(string s, IFormatProvider? provider = null)
	{
		ArgumentNullException.ThrowIfNull(s);

		bool? ended = null;
		bool? isPrivate = null;
		SearchSyntax.Parse(s, (key, op, rawValue) =>
		{
			if (op is not (":" or "=") || !SearchSyntax.TryParseState(rawValue, out var state)) return false;
			switch (key)
			{
				case "ended":
					ended = state;
					return true;
				case "private":
					isPrivate = state;
					return true;
				default:
					return false;
			}
		});

		return new MatchQuery(ended, isPrivate);
	}

	public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider,
		[MaybeNullWhen(false)] out MatchQuery result)
	{
		result = s is null ? null : Parse(s, provider);
		return result is not null;
	}

	public MatchQuery LimitTo(UserPrivileges effective, UserRecord viewer)
	{
		ArgumentNullException.ThrowIfNull(viewer);
		return effective.Allows(UserPrivileges.Tournament) ? this : this with { VisibleTo = viewer.Id };
	}
}
using Basil.Server.Features.Irc;
using Basil.Server.Features.Multiplayer;

namespace Basil.Server.Host;

/// <summary>Forces every reply-constant holder to resolve its members from the locale catalog.</summary>
/// <remarks>
///     A holder's members are <c>static readonly</c> fields resolved by its static constructor, which
///     runs the first time any member of the class is touched. Touching one member is therefore
///     enough to resolve every member the class declares. Lives in <c>Host/</c>, not
///     <c>Shared/Localization/</c>, because it necessarily names every slice's reply-constant holder
///     by type -- exactly the kind of cross-slice knowledge that belongs at the composition root, not
///     in <c>Shared</c>.
/// </remarks>
internal static class LocaleTouch
{
	/// <summary>Touches every reply-constant holder, forcing each one's static constructor to run.</summary>
	public static void AllReplyHolders()
	{
		_ = MpReplies.CreateFailed;
		_ = IrcReplies.Welcome;
	}
}
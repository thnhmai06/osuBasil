using Basil.Server.Host;
using Basil.Server.Shared.Localization;

namespace Basil.Server.Tests.Shared.Localization;

/// <summary>
///     Covers the merged locale catalog's completeness: every key production code asks for exists,
///     and every key a fragment supplies is asked for by something.
/// </summary>
public class LocaleCatalogTests
{
	[Fact]
	public void EveryReferencedKeyExistsAndEveryKeyIsReferenced()
	{
		// Touching every reply-constant holder forces its static initializer, which is what
		// registers the keys production code actually asks for.
		LocaleTouch.AllReplyHolders();

		var missing = LocaleCatalog.ReferencedKeys.Except(LocaleCatalog.AllKeys).Order().ToArray();
		var orphaned = LocaleCatalog.AllKeys.Except(LocaleCatalog.ReferencedKeys).Order().ToArray();

		Assert.True(missing.Length == 0, $"locale is missing: {string.Join(", ", missing)}");
		Assert.True(orphaned.Length == 0, $"locale has unused keys: {string.Join(", ", orphaned)}");
	}
}
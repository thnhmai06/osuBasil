namespace Basil.Web.Routing.Api;

/// <summary>Shared OpenAPI description fragments reused across the api. host's route files.</summary>
internal static class RouteDocs
{
	/// <summary>
	///     Suffix fragment appended to admin-key-gated route descriptions, stating the `X-Admin-Key`
	///     request-header requirement.
	/// </summary>
	public const string AdminKeyNote = " Requires a valid `X-Admin-Key` request header.";
}
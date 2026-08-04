namespace Basil.Web.Routing.Api;

/// <summary>Shared OpenAPI description fragments reused across the api. host's route files.</summary>
internal static class RouteDocs
{
	/// <summary>
	///     Suffix fragment appended to admin-key-gated route descriptions, stating the
	///     `Authorization: Bearer` request-header requirement.
	/// </summary>
	public const string AdminKeyNote = " Requires a valid `Authorization: Bearer <admin-key>` request header.";
}
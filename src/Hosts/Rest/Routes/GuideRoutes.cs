using System.Net;
using Basil.Host.Shared.Documentation;
using Markdig;

namespace Basil.Host.Rest.Routes;

/// <summary>Serves the pages of the documentation that the parts of the server write about themselves.</summary>
internal static class GuideRoutes
{
	private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().UsePipeTables().DisableHtml().Build();

	public static void MapGuides(this IEndpointRouteBuilder app)
	{
		app.MapGet("/guides", (IEnumerable<IDocumentPage> pages) => Html("Guides",
				"<h1>Guides</h1><ul>" + string.Concat(pages.OrderBy(page => page.Slug).Select(page =>
					$"<li><a href=\"/guides/{page.Slug}\">{WebUtility.HtmlEncode(page.Title)}</a></li>")) + "</ul>"))
			.ExcludeFromDescription();
		app.MapGet("/guides/{slug}", (string slug, IEnumerable<IDocumentPage> pages) =>
				Find(pages, slug) is { } page
					? Html(page.Title, Markdown.ToHtml(page.Markdown(), Pipeline))
					: Results.NotFound())
			.ExcludeFromDescription();
		app.MapGet("/guides/{slug}.md", (string slug, IEnumerable<IDocumentPage> pages) =>
				Find(pages, slug) is { } page
					? Results.Text(page.Markdown(), "text/markdown; charset=utf-8")
					: Results.NotFound())
			.ExcludeFromDescription();
	}

	private static IDocumentPage? Find(IEnumerable<IDocumentPage> pages, string slug)
	{
		return pages.FirstOrDefault(page => page.Slug.Equals(slug, StringComparison.OrdinalIgnoreCase));
	}

	private static IResult Html(string title, string body)
	{
		return Results.Content($"<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\">" +
		                       "<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">" +
		                       $"<title>{WebUtility.HtmlEncode(title)}</title>" +
		                       "<style>body{font:16px/1.5 system-ui,sans-serif;max-width:60rem;margin:2rem auto;padding:0 1rem}" +
		                       "table{border-collapse:collapse}td,th{border:1px solid #8884;padding:.25rem .6rem;text-align:left}" +
		                       "code{background:#8882;padding:0 .2rem;border-radius:3px}</style></head>" +
		                       $"<body>{body}</body></html>", "text/html; charset=utf-8");
	}
}

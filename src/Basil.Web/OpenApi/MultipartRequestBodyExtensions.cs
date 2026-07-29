using Microsoft.OpenApi;

namespace Basil.Web.OpenApi;

/// <summary>
///     Declares a `multipart/form-data` request body for routes that read the upload by hand via
///     <c>HttpContext.Request.ReadFormAsync</c> (no bound `IFormFile` parameter for the default OpenAPI
///     generator to pick up), so the shape is more than prose in `.WithDescription`.
/// </summary>
internal static class MultipartRequestBodyExtensions
{
	public static RouteHandlerBuilder WithMultipartFileUpload(this RouteHandlerBuilder builder,
		string fieldName = "file")
	{
		return builder.AddOpenApiOperationTransformer((operation, _, _) =>
		{
			operation.RequestBody = new OpenApiRequestBody
			{
				Required = true,
				Content = new Dictionary<string, OpenApiMediaType>
				{
					["multipart/form-data"] = new()
					{
						Schema = new OpenApiSchema
						{
							Type = JsonSchemaType.Object,
							Required = new HashSet<string> { fieldName },
							Properties = new Dictionary<string, IOpenApiSchema>
							{
								[fieldName] = new OpenApiSchema { Type = JsonSchemaType.String, Format = "binary" }
							}
						}
					}
				}
			};

			return Task.CompletedTask;
		});
	}
}
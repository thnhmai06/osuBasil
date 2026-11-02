using Microsoft.AspNetCore.Http;
using Basil.Host.Rest.Http;
using Microsoft.AspNetCore.Http.Features;

namespace Basil.Host.Rest.Tests.Http;

/// <summary>An upload route lifts the request size limit for itself.</summary>
public sealed class UploadLimitTests
{
	private sealed class BodyLimit : IHttpMaxRequestBodySizeFeature
	{
		public bool IsReadOnly { get; init; }
		public long? MaxRequestBodySize { get; set; } = 1024;
	}

	[Fact]
	public void AllowedUpload_RaisesTheLimit()
	{
		var context = new DefaultHttpContext();
		var feature = new BodyLimit();
		context.Features.Set<IHttpMaxRequestBodySizeFeature>(feature);

		UploadLimit.Allow(context, 5000);

		Assert.Equal(5000, feature.MaxRequestBodySize);
	}

	[Fact]
	public void ALimitThatCanNoLongerChange_IsLeftAlone()
	{
		var context = new DefaultHttpContext();
		var feature = new BodyLimit { IsReadOnly = true };
		context.Features.Set<IHttpMaxRequestBodySizeFeature>(feature);

		UploadLimit.Allow(context, 5000);

		Assert.Equal(1024, feature.MaxRequestBodySize);
	}
}

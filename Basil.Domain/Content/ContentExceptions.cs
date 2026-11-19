namespace Basil.Domain.Content;

/// <summary>A rule of the content module was broken: assets, uploads, FAQ and menu content.</summary>
public abstract class ContentException(string message) : BasilException(message);

/// <summary>A rule of the main menu was broken: banners and the icon.</summary>
public abstract class MenuException(string message) : ContentException(message);

/// <summary>An address that must be an absolute HTTP or HTTPS URL is not.</summary>
public sealed class InvalidAddressException() : MenuException("The address must be absolute with an http or https scheme.");

/// <summary>A menu banner window ends before it starts.</summary>
public sealed class InvalidBannerWindowException(DateTimeOffset? startsAt, DateTimeOffset? endsAt)
	: MenuException("A banner cannot end before it starts.")
{
	public DateTimeOffset? StartsAt { get; } = startsAt;

	public DateTimeOffset? EndsAt { get; } = endsAt;
}

/// <summary>A rule of the uploads the server accepts was broken.</summary>
public abstract class UploadException(string message) : ContentException(message);

/// <summary>An uploaded picture cannot be decoded or has no size.</summary>
public sealed class InvalidImageException() : UploadException("The image cannot be read.");

/// <summary>An upload is over the size the server accepts for it.</summary>
public sealed class UploadTooLargeException(long limit) : UploadException("The upload is too large.")
{
	public long Limit { get; } = limit;
}

/// <summary>An uploaded picture is wider or taller than the server accepts.</summary>
public sealed class ImageDimensionsTooLargeException(int maxSide) : UploadException("The image is too large in width or height.")
{
	public int MaxSide { get; } = maxSide;
}

/// <summary>A rule for the name of a stored piece of content was broken.</summary>
public abstract class ContentNameException(string message) : ContentException(message);

/// <summary>A file name cannot be stored: it is empty, holds a path or a forbidden character.</summary>
public sealed class InvalidFileNameException() : ContentNameException("The name is not a valid stored file name.");

/// <summary>A FAQ entry name, or a part of it, names nothing valid.</summary>
public sealed class InvalidFaqNameException() : ContentNameException("An entry name and each part of it must name something.");

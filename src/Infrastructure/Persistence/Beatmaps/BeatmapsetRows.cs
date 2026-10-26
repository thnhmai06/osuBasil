using Basil.Domain.Beatmaps;
using Basil.Domain.Common;
using Basil.Domain.Content;

namespace Basil.Infrastructure.Persistence.Beatmaps;

/// <summary>Builds stored beatmapsets from their rows.</summary>
internal static class BeatmapsetRows
{
	extension(BeatmapsetRow row)
	{
		/// <summary>Builds the beatmapset a row holds.</summary>
		/// <returns>The beatmapset, with the values it was stored with.</returns>
		public BeatmapsetRecord ToRecord()
		{
			return new BeatmapsetRecord
			{
				Id = row.Id,
				Fingerprint = new ContentFingerprint(new ContentStamp(row.ArchiveSize, row.ArchiveModifiedAt),
					new Md5(row.ArchiveHash)),
				Metadata = new BeatmapsetMetadata { Artist = row.Artist, Title = row.Title, Creator = row.Creator },
				Assets = new BeatmapsetAssets { HasStoryboard = row.Storyboard },
				CreatedAt = row.CreatedAt,
				UpdatedAt = row.UpdatedAt,
				Locked = row.Locked,
				Visible = row.Visible
			};
		}
	}
}
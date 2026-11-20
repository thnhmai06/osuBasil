using Basil.Domain.Beatmaps;
using Basil.Domain.Content;
using Basil.Shared.Structures.Cryptography;

namespace Basil.Infrastructure.Beatmaps.Persistence;

internal static class BeatmapsetRows
{
	extension(BeatmapsetRow row)
	{
		public BeatmapsetRecord ToRecord()
		{
			return new BeatmapsetRecord
			{
				Id = row.Id,
				ContentId = new ContentIdentifier(new ContentMetadata(row.ArchiveSize, row.ArchiveModifiedAt),
					new Md5Hash(row.ArchiveHash)),
				Metadata = new BeatmapsetMetadata { Artist = row.Artist, Title = row.Title, Creator = row.Creator },
				Assets = new BeatmapsetAssets { HasStoryboard = row.Storyboard },
				CreatedAt = row.CreatedAt,
				UpdatedAt = row.UpdatedAt,
				Locked = row.Locked,
				Visible = row.Visible,
				Status = row.Status
			};
		}
	}
}
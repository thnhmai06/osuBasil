-- Beatmapset archives: the set's own archive and the files each difficulty serves from it.
-- archive_hash is the MD5 of the archive, which identifies its content when a set is scanned.

alter table beatmapsets
	add archive_hash text null,
	add archive_size bigint null,
	add archive_modified_at timestamptz null,
	add storyboard boolean not null default false;

-- preview_time is in milliseconds, like beatmaps.length.
alter table beatmaps
	add background text null,
	add audio text null,
	add video text null,
	add preview_time integer null;

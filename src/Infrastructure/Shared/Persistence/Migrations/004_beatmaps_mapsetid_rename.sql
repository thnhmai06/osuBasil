-- Renames Beatmaps.MapsetId to BeatmapsetId, matching the domain/API naming everywhere else
-- (Beatmapset, not Mapset) -- Issue #4 CRITICAL naming consistency item. SQLite's RENAME COLUMN
-- updates the foreign key definition and dependent index automatically, so only the index's own
-- name needs a manual drop/recreate to stay consistent.
alter table Beatmaps
	rename column MapsetId to BeatmapsetId;

drop index Beatmaps_MapsetId_index;
create index Beatmaps_BeatmapsetId_index on Beatmaps (BeatmapsetId);

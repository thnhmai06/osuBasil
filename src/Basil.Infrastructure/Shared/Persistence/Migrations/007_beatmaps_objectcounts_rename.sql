-- Renames Beatmaps.ObjectCounts to Objects, matching Basil.Domain.Beatmaps.BeatmapObjects (the
-- ObjectCounts type hierarchy itself was renamed to *Objects at the same time -- OsuObjectCounts to
-- OsuObjects and so on). No index touches this column, so a plain rename is enough.
alter table Beatmaps
	rename column ObjectCounts to Objects;

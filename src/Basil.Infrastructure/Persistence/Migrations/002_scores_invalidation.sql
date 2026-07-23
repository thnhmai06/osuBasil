-- A score row is never hard-deleted when its beatmap changes or disappears (deleting a player's own
-- historical record over a metadata typo fix, or a difficulty removed from a mapset, is too
-- destructive) — it's flagged instead. IsInvalidated scores stay fully readable (including their
-- .osr replay) everywhere; only the ingestion cascade (BeatmapIngestionService) ever sets this.
alter table Scores
    add column IsInvalidated boolean default false not null;

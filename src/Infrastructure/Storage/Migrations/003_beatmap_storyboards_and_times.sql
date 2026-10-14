-- A difficulty's own storyboard, and when the difficulty was first and last imported.
-- Existing rows take the times of their beatmapset.

alter table beatmaps
	add storyboard boolean not null default false,
	add created_at timestamptz null,
	add updated_at timestamptz null;

update beatmaps b
set created_at = s.created_at,
	updated_at = s.updated_at
from beatmapsets s
where s.id = b.beatmapset_id;

alter table beatmaps
	alter column created_at set not null,
	alter column updated_at set not null;

-- Adds a dedicated soft-delete marker to Users. Deletion status now reads from this column
-- instead of being inferred from Privilege = 0, since Privilege = 0 was never a reliable signal
-- (nothing prevented a live account from holding it) and login never actually checked it.
alter table Users
	add column DeletedAt datetime null;

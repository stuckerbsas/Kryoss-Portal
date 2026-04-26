-- 068: Per-machine local administrators (JSON column)
ALTER TABLE machines ADD local_admins_json NVARCHAR(MAX) NULL;

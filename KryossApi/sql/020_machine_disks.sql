-- 020_machine_disks.sql
-- Multi-disk inventory: one row per drive letter per machine.

CREATE TABLE machine_disks (
    id              BIGINT IDENTITY(1,1) PRIMARY KEY,
    machine_id      UNIQUEIDENTIFIER NOT NULL REFERENCES machines(id),
    drive_letter    CHAR(1) NOT NULL,
    label           NVARCHAR(100),
    disk_type       VARCHAR(10),
    total_gb        INT,
    free_gb         DECIMAL(8,2),
    file_system     VARCHAR(10),
    updated_at      DATETIME2(2) NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT uq_machine_disk UNIQUE (machine_id, drive_letter)
);
CREATE INDEX ix_machine_disks_machine ON machine_disks(machine_id);

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO
-- ============================================================
-- 014_snapshot_rawdata.sql
-- Kryoss Platform -- Rich raw-data snapshot columns
--
-- Design principle: the agent is DUMB. It collects raw state and
-- sends it to the portal as-is. The portal evaluates rules
-- server-side against the stored snapshot. This lets rules change
-- without rescanning machines.
--
-- machine_snapshots gets five new JSON columns, each storing a
-- specific domain of raw state captured during an assessment run:
--
--   raw_hardware         -- BIOS, CPU, RAM, disks, GPU, monitors, NICs
--   raw_security_posture -- TPM, SecureBoot, BitLocker, Cred Guard, HVCI, DMA
--   raw_software         -- installed programs + store apps + hotfixes
--   raw_network          -- IP config, firewall profiles, open ports
--   raw_users            -- local/AD users, groups, SIDs, logon history
--
-- All columns are nullable so agents can emit partial payloads;
-- portal queries check ISJSON() + JSON_VALUE() to read fields.
-- ============================================================

-- Link snapshot to the assessment run that produced it
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('machine_snapshots') AND name = 'assessment_run_id')
    ALTER TABLE machine_snapshots ADD assessment_run_id UNIQUEIDENTIFIER NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'fk_snapshot_run')
    ALTER TABLE machine_snapshots
        ADD CONSTRAINT fk_snapshot_run FOREIGN KEY (assessment_run_id) REFERENCES assessment_runs(id);
GO

-- Raw hardware: BIOS, CPU, RAM (slots), disks, GPU, monitors, NICs, battery
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('machine_snapshots') AND name = 'raw_hardware')
    ALTER TABLE machine_snapshots ADD raw_hardware NVARCHAR(MAX) NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'ck_snap_hardware_json')
    ALTER TABLE machine_snapshots
        ADD CONSTRAINT ck_snap_hardware_json CHECK (ISJSON(raw_hardware) = 1 OR raw_hardware IS NULL);
GO

-- Raw security posture: TPM, SecureBoot, BitLocker per-volume, Credential Guard,
-- HVCI, DMA protection, Memory Integrity, Kernel DMA, etc.
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('machine_snapshots') AND name = 'raw_security_posture')
    ALTER TABLE machine_snapshots ADD raw_security_posture NVARCHAR(MAX) NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'ck_snap_secposture_json')
    ALTER TABLE machine_snapshots
        ADD CONSTRAINT ck_snap_secposture_json CHECK (ISJSON(raw_security_posture) = 1 OR raw_security_posture IS NULL);
GO

-- Raw software: full installed software list (Win32 uninstall keys + UWP/Store apps + hotfixes)
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('machine_snapshots') AND name = 'raw_software')
    ALTER TABLE machine_snapshots ADD raw_software NVARCHAR(MAX) NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'ck_snap_software_json')
    ALTER TABLE machine_snapshots
        ADD CONSTRAINT ck_snap_software_json CHECK (ISJSON(raw_software) = 1 OR raw_software IS NULL);
GO

-- Raw network: NICs, IPs, DNS, routes, firewall profiles, open listening ports
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('machine_snapshots') AND name = 'raw_network')
    ALTER TABLE machine_snapshots ADD raw_network NVARCHAR(MAX) NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'ck_snap_network_json')
    ALTER TABLE machine_snapshots
        ADD CONSTRAINT ck_snap_network_json CHECK (ISJSON(raw_network) = 1 OR raw_network IS NULL);
GO

-- Raw users: local/AD users, groups, group members, SIDs, logon history
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('machine_snapshots') AND name = 'raw_users')
    ALTER TABLE machine_snapshots ADD raw_users NVARCHAR(MAX) NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'ck_snap_users_json')
    ALTER TABLE machine_snapshots
        ADD CONSTRAINT ck_snap_users_json CHECK (ISJSON(raw_users) = 1 OR raw_users IS NULL);
GO

-- Agent + collection metadata
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('machine_snapshots') AND name = 'agent_version')
    ALTER TABLE machine_snapshots ADD agent_version VARCHAR(20) NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('machine_snapshots') AND name = 'collection_duration_ms')
    ALTER TABLE machine_snapshots ADD collection_duration_ms INT NULL;
GO

-- Indexes for lookups
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'ix_snapshots_run')
    CREATE INDEX ix_snapshots_run ON machine_snapshots(assessment_run_id) WHERE assessment_run_id IS NOT NULL;
GO

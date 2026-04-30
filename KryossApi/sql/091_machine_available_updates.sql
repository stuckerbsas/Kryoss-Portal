-- 091: Machine Available Updates (WUC-02)
-- Stores pending Windows Updates per machine with history tracking.

IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'machine_available_updates')
BEGIN
    CREATE TABLE machine_available_updates (
        id                  BIGINT          IDENTITY PRIMARY KEY,
        machine_id          UNIQUEIDENTIFIER NOT NULL REFERENCES machines(id) ON DELETE CASCADE,
        organization_id     UNIQUEIDENTIFIER NOT NULL REFERENCES organizations(id),
        kb_number           VARCHAR(20)     NOT NULL,
        title               NVARCHAR(500)   NOT NULL,
        severity            VARCHAR(20),
        classification      VARCHAR(100),
        is_mandatory        BIT             DEFAULT 0,
        max_download_size   BIGINT,
        release_date        DATETIME2,
        support_url         NVARCHAR(500),
        detected_at         DATETIME2       DEFAULT GETUTCDATE(),
        installed_at        DATETIME2       NULL,
        is_pending          BIT             DEFAULT 1
    );

    CREATE INDEX ix_mau_machine_pending
        ON machine_available_updates (machine_id, is_pending)
        WHERE is_pending = 1;

    CREATE INDEX ix_mau_kb
        ON machine_available_updates (kb_number);

    CREATE INDEX ix_mau_org_pending
        ON machine_available_updates (organization_id, is_pending)
        WHERE is_pending = 1;
END

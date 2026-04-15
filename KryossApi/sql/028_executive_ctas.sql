-- =============================================================================
-- Migration 028: executive_ctas — persistence for C-Level CTAs
--
-- Hybrid CTA model: the C-Level report Block 3 auto-detects up to 12 rules
-- from assessment data; the operator can edit, suppress, or add manual CTAs
-- before exporting. This table persists those edits per org per period so
-- the next generate call can replay them.
--
-- Idempotent: safe to run multiple times.
-- =============================================================================

SET NOCOUNT ON;

IF OBJECT_ID(N'dbo.executive_ctas', N'U') IS NULL
BEGIN
    CREATE TABLE executive_ctas (
        id                  UNIQUEIDENTIFIER NOT NULL
                            CONSTRAINT pk_executive_ctas PRIMARY KEY DEFAULT NEWID(),
        organization_id     UNIQUEIDENTIFIER NOT NULL,
        period_start        DATETIME2(2)     NOT NULL,
        auto_detected_rule  NVARCHAR(100)    NULL,
        priority_category   VARCHAR(20)      NOT NULL
                            CONSTRAINT ck_executive_ctas_category
                            CHECK (priority_category IN ('Incidentes','Hardening','Budget','Risk')),
        title               NVARCHAR(200)    NOT NULL,
        description         NVARCHAR(2000)   NOT NULL,
        is_suppressed       BIT              NOT NULL
                            CONSTRAINT df_executive_ctas_suppressed DEFAULT(0),
        is_manual           BIT              NOT NULL
                            CONSTRAINT df_executive_ctas_manual DEFAULT(0),
        created_by          UNIQUEIDENTIFIER NOT NULL,
        created_at          DATETIME2(2)     NOT NULL,
        modified_by         UNIQUEIDENTIFIER NULL,
        modified_at         DATETIME2(2)     NULL,
        deleted_by          UNIQUEIDENTIFIER NULL,
        deleted_at          DATETIME2(2)     NULL,
        CONSTRAINT fk_executive_ctas_org
            FOREIGN KEY (organization_id) REFERENCES organizations(id)
    );

    CREATE INDEX ix_executive_ctas_org_period
        ON executive_ctas (organization_id, period_start)
        WHERE deleted_at IS NULL;

    PRINT 'executive_ctas table created';
END
ELSE
    PRINT 'executive_ctas already exists, skipping';

GO

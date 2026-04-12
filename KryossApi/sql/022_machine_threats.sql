SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
GO

CREATE TABLE machine_threats (
    id              BIGINT IDENTITY(1,1) PRIMARY KEY,
    machine_id      UNIQUEIDENTIFIER NOT NULL REFERENCES machines(id),
    threat_name     NVARCHAR(200) NOT NULL,
    category        VARCHAR(50) NOT NULL,       -- browser_hijacker, adware, stalkerware, keylogger, rat, c2_tool, cryptominer, ransomware, fake_av, loader_stealer, employee_monitor, pup
    severity        VARCHAR(20) NOT NULL,       -- critical, high, medium, low, info
    vector          VARCHAR(20) NOT NULL,       -- registry, process
    detail          NVARCHAR(500),
    detected_at     DATETIME2(2) NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT uq_machine_threat UNIQUE (machine_id, threat_name, vector)
);
CREATE INDEX ix_machine_threats_machine ON machine_threats(machine_id);
CREATE INDEX ix_machine_threats_severity ON machine_threats(severity) WHERE severity IN ('critical', 'high');
GO

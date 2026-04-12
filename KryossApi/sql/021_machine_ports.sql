SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET XACT_ABORT ON;
GO

CREATE TABLE machine_ports (
    id              BIGINT IDENTITY(1,1) PRIMARY KEY,
    machine_id      UNIQUEIDENTIFIER NOT NULL REFERENCES machines(id),
    port            INT NOT NULL,
    protocol        VARCHAR(5) NOT NULL,       -- TCP, UDP
    status          VARCHAR(20) NOT NULL,       -- open, closed, filtered
    service         VARCHAR(50),               -- HTTP, SSH, RDP, etc.
    risk            VARCHAR(20),               -- critical, high, medium, low, null
    scanned_at      DATETIME2(2) NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT uq_machine_port UNIQUE (machine_id, port, protocol)
);
CREATE INDEX ix_machine_ports_machine ON machine_ports(machine_id);
CREATE INDEX ix_machine_ports_risk ON machine_ports(risk) WHERE risk IS NOT NULL;
GO

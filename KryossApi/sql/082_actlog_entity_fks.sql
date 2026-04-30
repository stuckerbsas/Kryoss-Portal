-- 082: Add machine_id and user_id FK columns to actlog
-- Enables filtering actlog by machine (activity tab) and by user

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('actlog') AND name = 'machine_id')
BEGIN
    ALTER TABLE actlog ADD machine_id UNIQUEIDENTIFIER NULL;
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('actlog') AND name = 'user_id')
BEGIN
    ALTER TABLE actlog ADD user_id UNIQUEIDENTIFIER NULL;
END
GO

-- FK to machines (NO ACTION — actlog has INSTEAD OF triggers, can't use SET NULL/CASCADE)
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_actlog_machines')
BEGIN
    ALTER TABLE actlog
    ADD CONSTRAINT FK_actlog_machines
    FOREIGN KEY (machine_id) REFERENCES machines(id) ON DELETE NO ACTION;
END
GO

-- FK to users (NO ACTION)
IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_actlog_users')
BEGIN
    ALTER TABLE actlog
    ADD CONSTRAINT FK_actlog_users
    FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE NO ACTION;
END
GO

-- Indexes for filtering
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_actlog_machine_id' AND object_id = OBJECT_ID('actlog'))
    CREATE INDEX IX_actlog_machine_id ON actlog(machine_id) WHERE machine_id IS NOT NULL;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_actlog_user_id' AND object_id = OBJECT_ID('actlog'))
    CREATE INDEX IX_actlog_user_id ON actlog(user_id) WHERE user_id IS NOT NULL;
GO

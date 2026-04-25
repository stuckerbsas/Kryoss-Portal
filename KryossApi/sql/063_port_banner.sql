-- 063: Add banner grab fields to machine_ports
-- Supports: service fingerprinting from port scanner

ALTER TABLE machine_ports ADD
    banner varchar(512) NULL,
    service_name varchar(100) NULL,
    service_version varchar(100) NULL;

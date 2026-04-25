-- Seed: SNMP device profiles — vendor-specific OIDs
-- sysObjectId enterprise prefixes: https://www.iana.org/assignments/enterprise-numbers/

-- ═══════════════════════════════════════════════════════
-- Fortinet (1.3.6.1.4.1.12356)
-- ═══════════════════════════════════════════════════════
IF NOT EXISTS (SELECT 1 FROM snmp_device_profiles WHERE oid_prefix = '1.3.6.1.4.1.12356')
BEGIN
    INSERT INTO snmp_device_profiles (vendor_name, oid_prefix) VALUES ('Fortinet', '1.3.6.1.4.1.12356');
    DECLARE @fortinet INT = SCOPE_IDENTITY();

    INSERT INTO snmp_profile_oids (profile_id, oid, name, category, data_type, unit, walk) VALUES
    (@fortinet, '1.3.6.1.4.1.12356.101.4.1.3.0',  'cpu_usage',         'performance', 'gauge',   '%',       0),
    (@fortinet, '1.3.6.1.4.1.12356.101.4.1.4.0',  'memory_usage',      'performance', 'gauge',   '%',       0),
    (@fortinet, '1.3.6.1.4.1.12356.101.4.1.8.0',  'session_count',     'performance', 'gauge',   'sessions',0),
    (@fortinet, '1.3.6.1.4.1.12356.101.4.1.11.0', 'disk_usage',        'performance', 'gauge',   '%',       0),
    (@fortinet, '1.3.6.1.4.1.12356.101.13.2.1',   'ha_status',         'security',    'string',  NULL,      1),
    (@fortinet, '1.3.6.1.4.1.12356.101.12.2.3.1',  'vpn_tunnels',      'vpn',         'string',  NULL,      1),
    (@fortinet, '1.3.6.1.4.1.12356.101.4.1.1.0',  'serial_number',     'inventory',   'string',  NULL,      0),
    (@fortinet, '1.3.6.1.4.1.12356.101.4.1.7.0',  'firmware_version',  'inventory',   'string',  NULL,      0);
END

-- ═══════════════════════════════════════════════════════
-- Cisco (1.3.6.1.4.1.9)
-- ═══════════════════════════════════════════════════════
IF NOT EXISTS (SELECT 1 FROM snmp_device_profiles WHERE oid_prefix = '1.3.6.1.4.1.9')
BEGIN
    INSERT INTO snmp_device_profiles (vendor_name, oid_prefix) VALUES ('Cisco', '1.3.6.1.4.1.9');
    DECLARE @cisco INT = SCOPE_IDENTITY();

    INSERT INTO snmp_profile_oids (profile_id, oid, name, category, data_type, unit, walk) VALUES
    (@cisco, '1.3.6.1.4.1.9.9.109.1.1.1.1.8',   'cpu_5min',           'performance', 'gauge',   '%',       1),
    (@cisco, '1.3.6.1.4.1.9.9.48.1.1.1.5',       'mem_pool_used',      'performance', 'gauge',   'bytes',   1),
    (@cisco, '1.3.6.1.4.1.9.9.48.1.1.1.6',       'mem_pool_free',      'performance', 'gauge',   'bytes',   1),
    (@cisco, '1.3.6.1.4.1.9.9.25.1.1.1.2.3',     'flash_size',         'inventory',   'gauge',   'bytes',   0),
    (@cisco, '1.3.6.1.4.1.9.9.46.1.3.1.1.4',     'vlan_state',         'inventory',   'string',  NULL,      1),
    (@cisco, '1.3.6.1.4.1.9.9.500.1.2.1.1.1',    'stack_role',         'inventory',   'string',  NULL,      1),
    (@cisco, '1.3.6.1.4.1.9.9.13.1.3.1.3',       'temperature',        'performance', 'gauge',   'C',       1),
    (@cisco, '1.3.6.1.4.1.9.9.13.1.5.1.3',       'power_supply_state', 'performance', 'gauge',   NULL,      1);
END

-- ═══════════════════════════════════════════════════════
-- Meraki (1.3.6.1.4.1.29671)
-- ═══════════════════════════════════════════════════════
IF NOT EXISTS (SELECT 1 FROM snmp_device_profiles WHERE oid_prefix = '1.3.6.1.4.1.29671')
BEGIN
    INSERT INTO snmp_device_profiles (vendor_name, oid_prefix) VALUES ('Meraki', '1.3.6.1.4.1.29671');
    DECLARE @meraki INT = SCOPE_IDENTITY();

    INSERT INTO snmp_profile_oids (profile_id, oid, name, category, data_type, unit, walk) VALUES
    (@meraki, '1.3.6.1.4.1.29671.1.1.4.1.2',    'dev_name',          'inventory',   'string',  NULL,      1),
    (@meraki, '1.3.6.1.4.1.29671.1.1.4.1.3',     'dev_status',       'performance', 'gauge',   NULL,      1),
    (@meraki, '1.3.6.1.4.1.29671.1.1.4.1.5',     'dev_serial',       'inventory',   'string',  NULL,      1),
    (@meraki, '1.3.6.1.4.1.29671.1.1.4.1.9',     'dev_model',        'inventory',   'string',  NULL,      1),
    (@meraki, '1.3.6.1.4.1.29671.1.1.4.1.11',    'dev_wan_ip',       'inventory',   'string',  NULL,      1);
END

-- ═══════════════════════════════════════════════════════
-- Ubiquiti (1.3.6.1.4.1.41112)
-- ═══════════════════════════════════════════════════════
IF NOT EXISTS (SELECT 1 FROM snmp_device_profiles WHERE oid_prefix = '1.3.6.1.4.1.41112')
BEGIN
    INSERT INTO snmp_device_profiles (vendor_name, oid_prefix) VALUES ('Ubiquiti', '1.3.6.1.4.1.41112');
    DECLARE @ubiquiti INT = SCOPE_IDENTITY();

    INSERT INTO snmp_profile_oids (profile_id, oid, name, category, data_type, unit, walk) VALUES
    (@ubiquiti, '1.3.6.1.4.1.41112.1.6.3.3',     'radio_rssi',       'wireless',    'gauge',   'dBm',     1),
    (@ubiquiti, '1.3.6.1.4.1.41112.1.6.3.6',      'radio_ccq',       'wireless',    'gauge',   '%',       1),
    (@ubiquiti, '1.3.6.1.4.1.41112.1.6.3.2',      'radio_freq',      'wireless',    'gauge',   'MHz',     1),
    (@ubiquiti, '1.3.6.1.4.1.41112.1.6.1.1',      'station_count',   'wireless',    'gauge',   'clients', 0);
END

-- ═══════════════════════════════════════════════════════
-- Palo Alto (1.3.6.1.4.1.25461)
-- ═══════════════════════════════════════════════════════
IF NOT EXISTS (SELECT 1 FROM snmp_device_profiles WHERE oid_prefix = '1.3.6.1.4.1.25461')
BEGIN
    INSERT INTO snmp_device_profiles (vendor_name, oid_prefix) VALUES ('Palo Alto', '1.3.6.1.4.1.25461');
    DECLARE @paloalto INT = SCOPE_IDENTITY();

    INSERT INTO snmp_profile_oids (profile_id, oid, name, category, data_type, unit, walk) VALUES
    (@paloalto, '1.3.6.1.4.1.25461.2.1.2.3.1.0',  'session_count',   'performance', 'gauge',   'sessions',0),
    (@paloalto, '1.3.6.1.4.1.25461.2.1.2.3.3.0',  'session_max',     'performance', 'gauge',   'sessions',0),
    (@paloalto, '1.3.6.1.4.1.25461.2.1.2.1.1.0',  'firmware_version','inventory',   'string',  NULL,      0),
    (@paloalto, '1.3.6.1.4.1.25461.2.1.2.3.9.0',  'gp_active_tunnels','vpn',        'gauge',   'tunnels', 0);
END

-- ═══════════════════════════════════════════════════════
-- SonicWall (1.3.6.1.4.1.8741)
-- ═══════════════════════════════════════════════════════
IF NOT EXISTS (SELECT 1 FROM snmp_device_profiles WHERE oid_prefix = '1.3.6.1.4.1.8741')
BEGIN
    INSERT INTO snmp_device_profiles (vendor_name, oid_prefix) VALUES ('SonicWall', '1.3.6.1.4.1.8741');
    DECLARE @sonicwall INT = SCOPE_IDENTITY();

    INSERT INTO snmp_profile_oids (profile_id, oid, name, category, data_type, unit, walk) VALUES
    (@sonicwall, '1.3.6.1.4.1.8741.1.3.1.3.0',    'cpu_usage',       'performance', 'gauge',   '%',       0),
    (@sonicwall, '1.3.6.1.4.1.8741.1.3.1.4.0',     'ram_usage',      'performance', 'gauge',   '%',       0),
    (@sonicwall, '1.3.6.1.4.1.8741.1.3.1.1.0',     'connection_count','performance', 'gauge',   'sessions',0),
    (@sonicwall, '1.3.6.1.4.1.8741.1.3.1.2.0',     'connection_max',  'performance', 'gauge',   'sessions',0);
END

-- ═══════════════════════════════════════════════════════
-- MikroTik (1.3.6.1.4.1.14988)
-- ═══════════════════════════════════════════════════════
IF NOT EXISTS (SELECT 1 FROM snmp_device_profiles WHERE oid_prefix = '1.3.6.1.4.1.14988')
BEGIN
    INSERT INTO snmp_device_profiles (vendor_name, oid_prefix) VALUES ('MikroTik', '1.3.6.1.4.1.14988');
    DECLARE @mikrotik INT = SCOPE_IDENTITY();

    INSERT INTO snmp_profile_oids (profile_id, oid, name, category, data_type, unit, walk) VALUES
    (@mikrotik, '1.3.6.1.4.1.14988.1.1.3.100.1.3', 'wireless_clients', 'wireless',  'gauge',   'clients', 1),
    (@mikrotik, '1.3.6.1.4.1.14988.1.1.3.100.1.7', 'wireless_freq',    'wireless',  'gauge',   'MHz',     1),
    (@mikrotik, '1.3.6.1.2.1.25.3.3.1.2',           'cpu_usage',       'performance','gauge',   '%',       1),
    (@mikrotik, '1.3.6.1.2.1.25.2.3.1.6',           'storage_used',    'performance','gauge',   'bytes',   1);
END

-- ═══════════════════════════════════════════════════════
-- APC UPS (1.3.6.1.4.1.318)
-- ═══════════════════════════════════════════════════════
IF NOT EXISTS (SELECT 1 FROM snmp_device_profiles WHERE oid_prefix = '1.3.6.1.4.1.318')
BEGIN
    INSERT INTO snmp_device_profiles (vendor_name, oid_prefix) VALUES ('APC', '1.3.6.1.4.1.318');
    DECLARE @apc INT = SCOPE_IDENTITY();

    INSERT INTO snmp_profile_oids (profile_id, oid, name, category, data_type, unit, walk) VALUES
    (@apc, '1.3.6.1.4.1.318.1.1.1.2.2.1.0',  'battery_capacity',   'performance', 'gauge',   '%',       0),
    (@apc, '1.3.6.1.4.1.318.1.1.1.2.2.3.0',   'battery_runtime',   'performance', 'timeticks','minutes', 0),
    (@apc, '1.3.6.1.4.1.318.1.1.1.2.2.2.0',   'battery_temperature','performance', 'gauge',   'C',       0),
    (@apc, '1.3.6.1.4.1.318.1.1.1.3.2.1.0',   'input_voltage',     'performance', 'gauge',   'V',       0),
    (@apc, '1.3.6.1.4.1.318.1.1.1.4.2.1.0',   'output_voltage',    'performance', 'gauge',   'V',       0),
    (@apc, '1.3.6.1.4.1.318.1.1.1.4.2.3.0',   'output_load',       'performance', 'gauge',   '%',       0),
    (@apc, '1.3.6.1.4.1.318.1.1.1.1.1.1.0',   'ups_model',         'inventory',   'string',  NULL,      0),
    (@apc, '1.3.6.1.4.1.318.1.1.1.1.2.3.0',   'firmware_version',  'inventory',   'string',  NULL,      0);
END

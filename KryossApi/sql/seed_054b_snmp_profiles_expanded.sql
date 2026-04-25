-- Seed 054b: Expanded SNMP vendor profiles
-- Adds HP/Aruba, Synology, QNAP, Eaton, CyberPower, WatchGuard, Netgear, Ruckus, Dell, Juniper

-- ═══════════════════════════════════════════════════════
-- HP / HPE / Aruba switches (1.3.6.1.4.1.11)
-- ═══════════════════════════════════════════════════════
IF NOT EXISTS (SELECT 1 FROM snmp_device_profiles WHERE oid_prefix = '1.3.6.1.4.1.11')
BEGIN
    INSERT INTO snmp_device_profiles (vendor_name, oid_prefix) VALUES ('HP', '1.3.6.1.4.1.11');
    DECLARE @hp INT = SCOPE_IDENTITY();

    INSERT INTO snmp_profile_oids (profile_id, oid, name, category, data_type, unit, walk) VALUES
    (@hp, '1.3.6.1.4.1.11.2.14.11.5.1.9.6.1.0',  'cpu_usage',         'performance', 'gauge',   '%',       0),
    (@hp, '1.3.6.1.4.1.11.2.14.11.5.1.1.2.2.1.1.5','temperature',     'performance', 'gauge',   'C',       1),
    (@hp, '1.3.6.1.4.1.11.2.14.11.5.1.1.2.1.1.1.6','fan_state',       'performance', 'gauge',   NULL,      1),
    (@hp, '1.3.6.1.4.1.11.2.14.11.5.1.1.2.1.1.1.7','psu_state',       'performance', 'gauge',   NULL,      1),
    (@hp, '1.3.6.1.4.1.11.2.14.11.5.1.54.2.1.1.4', 'poe_power_used',  'performance', 'gauge',   'mW',      1),
    (@hp, '1.3.6.1.4.1.11.2.14.11.5.1.54.2.1.1.2', 'poe_power_max',   'performance', 'gauge',   'mW',      1),
    (@hp, '1.3.6.1.4.1.11.2.14.11.5.1.1.3.0',      'mem_total',       'performance', 'gauge',   'bytes',   0),
    (@hp, '1.3.6.1.4.1.11.2.14.11.5.1.1.4.0',      'mem_free',        'performance', 'gauge',   'bytes',   0);
END

-- ═══════════════════════════════════════════════════════
-- Aruba wireless controllers / APs (1.3.6.1.4.1.14823)
-- ═══════════════════════════════════════════════════════
IF NOT EXISTS (SELECT 1 FROM snmp_device_profiles WHERE oid_prefix = '1.3.6.1.4.1.14823')
BEGIN
    INSERT INTO snmp_device_profiles (vendor_name, oid_prefix) VALUES ('Aruba', '1.3.6.1.4.1.14823');
    DECLARE @aruba INT = SCOPE_IDENTITY();

    INSERT INTO snmp_profile_oids (profile_id, oid, name, category, data_type, unit, walk) VALUES
    (@aruba, '1.3.6.1.4.1.14823.2.2.1.1.1.9.0',    'cpu_usage',        'performance', 'gauge',   '%',       0),
    (@aruba, '1.3.6.1.4.1.14823.2.2.1.1.1.11.0',   'mem_total',        'performance', 'gauge',   'bytes',   0),
    (@aruba, '1.3.6.1.4.1.14823.2.2.1.1.1.12.0',   'mem_used',         'performance', 'gauge',   'bytes',   0),
    (@aruba, '1.3.6.1.4.1.14823.2.2.1.1.3.1.0',    'ap_count',         'wireless',    'gauge',   'APs',     0),
    (@aruba, '1.3.6.1.4.1.14823.2.2.1.1.3.2.0',    'station_count',    'wireless',    'gauge',   'clients', 0),
    (@aruba, '1.3.6.1.4.1.14823.2.2.1.5.2.1.5.1.2', 'ssid_name',       'wireless',    'string',  NULL,      1),
    (@aruba, '1.3.6.1.4.1.14823.2.2.1.5.2.1.5.1.7', 'ssid_clients',    'wireless',    'gauge',   'clients', 1);
END

-- ═══════════════════════════════════════════════════════
-- Synology NAS (1.3.6.1.4.1.6574)
-- ═══════════════════════════════════════════════════════
IF NOT EXISTS (SELECT 1 FROM snmp_device_profiles WHERE oid_prefix = '1.3.6.1.4.1.6574')
BEGIN
    INSERT INTO snmp_device_profiles (vendor_name, oid_prefix) VALUES ('Synology', '1.3.6.1.4.1.6574');
    DECLARE @synology INT = SCOPE_IDENTITY();

    INSERT INTO snmp_profile_oids (profile_id, oid, name, category, data_type, unit, walk) VALUES
    (@synology, '1.3.6.1.4.1.6574.1.2.0',            'system_status',     'performance', 'gauge',   NULL,     0),
    (@synology, '1.3.6.1.4.1.6574.1.5.1.0',           'cpu_usage_user',   'performance', 'gauge',   '%',      0),
    (@synology, '1.3.6.1.4.1.6574.1.5.2.0',           'cpu_usage_system', 'performance', 'gauge',   '%',      0),
    (@synology, '1.3.6.1.4.1.6574.1.5.5.0',           'memory_total',     'performance', 'gauge',   'bytes',  0),
    (@synology, '1.3.6.1.4.1.6574.1.5.6.0',           'memory_used',      'performance', 'gauge',   'bytes',  0),
    (@synology, '1.3.6.1.4.1.6574.2.1.1.2',           'raid_name',        'inventory',   'string',  NULL,     1),
    (@synology, '1.3.6.1.4.1.6574.2.1.1.3',           'raid_status',      'performance', 'gauge',   NULL,     1),
    (@synology, '1.3.6.1.4.1.6574.2.1.1.5',           'raid_free_size',   'performance', 'gauge',   'bytes',  1),
    (@synology, '1.3.6.1.4.1.6574.2.1.1.6',           'raid_total_size',  'performance', 'gauge',   'bytes',  1),
    (@synology, '1.3.6.1.4.1.6574.3.1.1.2',           'disk_model',       'inventory',   'string',  NULL,     1),
    (@synology, '1.3.6.1.4.1.6574.3.1.1.3',           'disk_type',        'inventory',   'string',  NULL,     1),
    (@synology, '1.3.6.1.4.1.6574.3.1.1.5',           'disk_status',      'performance', 'gauge',   NULL,     1),
    (@synology, '1.3.6.1.4.1.6574.3.1.1.6',           'disk_temperature', 'performance', 'gauge',   'C',      1),
    (@synology, '1.3.6.1.4.1.6574.1.1.0',             'model',            'inventory',   'string',  NULL,     0),
    (@synology, '1.3.6.1.4.1.6574.1.3.0',             'firmware_version', 'inventory',   'string',  NULL,     0);
END

-- ═══════════════════════════════════════════════════════
-- QNAP NAS (1.3.6.1.4.1.24681)
-- ═══════════════════════════════════════════════════════
IF NOT EXISTS (SELECT 1 FROM snmp_device_profiles WHERE oid_prefix = '1.3.6.1.4.1.24681')
BEGIN
    INSERT INTO snmp_device_profiles (vendor_name, oid_prefix) VALUES ('QNAP', '1.3.6.1.4.1.24681');
    DECLARE @qnap INT = SCOPE_IDENTITY();

    INSERT INTO snmp_profile_oids (profile_id, oid, name, category, data_type, unit, walk) VALUES
    (@qnap, '1.3.6.1.4.1.24681.1.2.1.0',            'cpu_usage',        'performance', 'gauge',   '%',      0),
    (@qnap, '1.3.6.1.4.1.24681.1.2.2.0',             'mem_total',        'performance', 'gauge',   'bytes',  0),
    (@qnap, '1.3.6.1.4.1.24681.1.2.3.0',             'mem_free',         'performance', 'gauge',   'bytes',  0),
    (@qnap, '1.3.6.1.4.1.24681.1.2.6.0',             'system_temperature','performance','gauge',   'C',      0),
    (@qnap, '1.3.6.1.4.1.24681.1.2.11.1.1.2',        'volume_descr',     'inventory',   'string',  NULL,     1),
    (@qnap, '1.3.6.1.4.1.24681.1.2.11.1.1.3',        'volume_total',     'performance', 'gauge',   'bytes',  1),
    (@qnap, '1.3.6.1.4.1.24681.1.2.11.1.1.4',        'volume_free',      'performance', 'gauge',   'bytes',  1),
    (@qnap, '1.3.6.1.4.1.24681.1.2.11.1.1.5',        'volume_status',    'performance', 'string',  NULL,     1),
    (@qnap, '1.3.6.1.4.1.24681.1.2.17.1.1.2',        'disk_model',       'inventory',   'string',  NULL,     1),
    (@qnap, '1.3.6.1.4.1.24681.1.2.17.1.1.3',        'disk_temperature', 'performance', 'gauge',   'C',      1),
    (@qnap, '1.3.6.1.4.1.24681.1.2.17.1.1.5',        'disk_size',        'inventory',   'gauge',   'bytes',  1),
    (@qnap, '1.3.6.1.4.1.24681.1.2.4.0',             'model',            'inventory',   'string',  NULL,     0),
    (@qnap, '1.3.6.1.4.1.24681.1.2.12.0',            'firmware_version', 'inventory',   'string',  NULL,     0);
END

-- ═══════════════════════════════════════════════════════
-- Eaton UPS (1.3.6.1.4.1.534)
-- ═══════════════════════════════════════════════════════
IF NOT EXISTS (SELECT 1 FROM snmp_device_profiles WHERE oid_prefix = '1.3.6.1.4.1.534')
BEGIN
    INSERT INTO snmp_device_profiles (vendor_name, oid_prefix) VALUES ('Eaton', '1.3.6.1.4.1.534');
    DECLARE @eaton INT = SCOPE_IDENTITY();

    INSERT INTO snmp_profile_oids (profile_id, oid, name, category, data_type, unit, walk) VALUES
    (@eaton, '1.3.6.1.4.1.534.1.2.1.0',              'battery_status',    'performance', 'gauge',  NULL,      0),
    (@eaton, '1.3.6.1.4.1.534.1.2.4.0',              'battery_capacity',  'performance', 'gauge',  '%',       0),
    (@eaton, '1.3.6.1.4.1.534.1.2.3.0',              'battery_runtime',   'performance', 'gauge',  'minutes', 0),
    (@eaton, '1.3.6.1.4.1.534.1.2.5.0',              'battery_voltage',   'performance', 'gauge',  'V',       0),
    (@eaton, '1.3.6.1.4.1.534.1.3.1.0',              'input_voltage',     'performance', 'gauge',  'V',       0),
    (@eaton, '1.3.6.1.4.1.534.1.4.1.0',              'output_voltage',    'performance', 'gauge',  'V',       0),
    (@eaton, '1.3.6.1.4.1.534.1.4.4.1.4',            'output_load',       'performance', 'gauge',  '%',       1),
    (@eaton, '1.3.6.1.4.1.534.1.1.2.0',              'ups_model',         'inventory',   'string', NULL,      0);
END

-- ═══════════════════════════════════════════════════════
-- CyberPower UPS (1.3.6.1.4.1.3808)
-- ═══════════════════════════════════════════════════════
IF NOT EXISTS (SELECT 1 FROM snmp_device_profiles WHERE oid_prefix = '1.3.6.1.4.1.3808')
BEGIN
    INSERT INTO snmp_device_profiles (vendor_name, oid_prefix) VALUES ('CyberPower', '1.3.6.1.4.1.3808');
    DECLARE @cyberpower INT = SCOPE_IDENTITY();

    INSERT INTO snmp_profile_oids (profile_id, oid, name, category, data_type, unit, walk) VALUES
    (@cyberpower, '1.3.6.1.4.1.3808.1.1.1.2.2.1.0',  'battery_capacity', 'performance', 'gauge',  '%',       0),
    (@cyberpower, '1.3.6.1.4.1.3808.1.1.1.2.2.4.0',  'battery_runtime',  'performance', 'gauge',  'minutes', 0),
    (@cyberpower, '1.3.6.1.4.1.3808.1.1.1.2.2.2.0',  'battery_voltage',  'performance', 'gauge',  'V',       0),
    (@cyberpower, '1.3.6.1.4.1.3808.1.1.1.3.2.1.0',  'input_voltage',    'performance', 'gauge',  'V',       0),
    (@cyberpower, '1.3.6.1.4.1.3808.1.1.1.4.2.1.0',  'output_voltage',   'performance', 'gauge',  'V',       0),
    (@cyberpower, '1.3.6.1.4.1.3808.1.1.1.4.2.3.0',  'output_load',      'performance', 'gauge',  '%',       0),
    (@cyberpower, '1.3.6.1.4.1.3808.1.1.1.1.1.1.0',  'ups_model',        'inventory',   'string', NULL,      0),
    (@cyberpower, '1.3.6.1.4.1.3808.1.1.1.1.2.1.0',  'firmware_version', 'inventory',   'string', NULL,      0);
END

-- ═══════════════════════════════════════════════════════
-- WatchGuard (1.3.6.1.4.1.3097)
-- ═══════════════════════════════════════════════════════
IF NOT EXISTS (SELECT 1 FROM snmp_device_profiles WHERE oid_prefix = '1.3.6.1.4.1.3097')
BEGIN
    INSERT INTO snmp_device_profiles (vendor_name, oid_prefix) VALUES ('WatchGuard', '1.3.6.1.4.1.3097');
    DECLARE @wg INT = SCOPE_IDENTITY();

    INSERT INTO snmp_profile_oids (profile_id, oid, name, category, data_type, unit, walk) VALUES
    (@wg, '1.3.6.1.4.1.3097.6.3.77.0',              'active_connections','performance', 'gauge',   'sessions',0),
    (@wg, '1.3.6.1.4.1.3097.6.3.78.0',              'cpu_usage',        'performance', 'gauge',   '%',       0),
    (@wg, '1.3.6.1.4.1.3097.6.3.80.0',              'active_vpn_tunnels','vpn',        'gauge',   'tunnels', 0);
END

-- ═══════════════════════════════════════════════════════
-- Netgear managed switches (1.3.6.1.4.1.4526)
-- ═══════════════════════════════════════════════════════
IF NOT EXISTS (SELECT 1 FROM snmp_device_profiles WHERE oid_prefix = '1.3.6.1.4.1.4526')
BEGIN
    INSERT INTO snmp_device_profiles (vendor_name, oid_prefix) VALUES ('Netgear', '1.3.6.1.4.1.4526');
    DECLARE @netgear INT = SCOPE_IDENTITY();

    INSERT INTO snmp_profile_oids (profile_id, oid, name, category, data_type, unit, walk) VALUES
    (@netgear, '1.3.6.1.4.1.4526.10.43.1.8.1.5',    'poe_port_power',   'performance', 'gauge',   'mW',     1),
    (@netgear, '1.3.6.1.4.1.4526.10.43.1.8.1.3',    'poe_port_status',  'performance', 'gauge',   NULL,     1),
    (@netgear, '1.3.6.1.4.1.4526.10.1.1.1.13.0',    'cpu_usage_5s',     'performance', 'gauge',   '%',      0);
END

-- ═══════════════════════════════════════════════════════
-- Ruckus wireless (1.3.6.1.4.1.25053)
-- ═══════════════════════════════════════════════════════
IF NOT EXISTS (SELECT 1 FROM snmp_device_profiles WHERE oid_prefix = '1.3.6.1.4.1.25053')
BEGIN
    INSERT INTO snmp_device_profiles (vendor_name, oid_prefix) VALUES ('Ruckus', '1.3.6.1.4.1.25053');
    DECLARE @ruckus INT = SCOPE_IDENTITY();

    INSERT INTO snmp_profile_oids (profile_id, oid, name, category, data_type, unit, walk) VALUES
    (@ruckus, '1.3.6.1.4.1.25053.1.2.2.1.1.1.15.1',  'ap_name',         'inventory',   'string',  NULL,     1),
    (@ruckus, '1.3.6.1.4.1.25053.1.2.2.1.1.1.15.2',   'ap_model',       'inventory',   'string',  NULL,     1),
    (@ruckus, '1.3.6.1.4.1.25053.1.2.2.1.1.1.15.10',  'ap_client_count','wireless',    'gauge',   'clients',1),
    (@ruckus, '1.3.6.1.4.1.25053.1.2.2.4.1.1.1.1',    'total_aps',      'wireless',    'gauge',   'APs',    0),
    (@ruckus, '1.3.6.1.4.1.25053.1.2.2.4.1.1.1.2',    'total_clients',  'wireless',    'gauge',   'clients',0);
END

-- ═══════════════════════════════════════════════════════
-- Dell / Dell EMC networking (1.3.6.1.4.1.674)
-- ═══════════════════════════════════════════════════════
IF NOT EXISTS (SELECT 1 FROM snmp_device_profiles WHERE oid_prefix = '1.3.6.1.4.1.674')
BEGIN
    INSERT INTO snmp_device_profiles (vendor_name, oid_prefix) VALUES ('Dell', '1.3.6.1.4.1.674');
    DECLARE @dell INT = SCOPE_IDENTITY();

    INSERT INTO snmp_profile_oids (profile_id, oid, name, category, data_type, unit, walk) VALUES
    (@dell, '1.3.6.1.4.1.674.10895.5000.2.6132.1.1.1.1.4.2.0','cpu_5sec',     'performance', 'gauge',   '%',  0),
    (@dell, '1.3.6.1.4.1.674.10895.5000.2.6132.1.1.1.1.4.4.0','cpu_1min',     'performance', 'gauge',   '%',  0),
    (@dell, '1.3.6.1.4.1.674.10895.5000.2.6132.1.1.1.1.5.1.0','mem_total',    'performance', 'gauge',   'KB', 0),
    (@dell, '1.3.6.1.4.1.674.10895.5000.2.6132.1.1.1.1.5.2.0','mem_available','performance', 'gauge',   'KB', 0),
    (@dell, '1.3.6.1.4.1.674.10895.5000.2.6132.1.1.43.1.8.1.5','poe_power',   'performance', 'gauge',   'mW', 1);
END

-- ═══════════════════════════════════════════════════════
-- Juniper Networks (1.3.6.1.4.1.2636)
-- ═══════════════════════════════════════════════════════
IF NOT EXISTS (SELECT 1 FROM snmp_device_profiles WHERE oid_prefix = '1.3.6.1.4.1.2636')
BEGIN
    INSERT INTO snmp_device_profiles (vendor_name, oid_prefix) VALUES ('Juniper', '1.3.6.1.4.1.2636');
    DECLARE @juniper INT = SCOPE_IDENTITY();

    INSERT INTO snmp_profile_oids (profile_id, oid, name, category, data_type, unit, walk) VALUES
    (@juniper, '1.3.6.1.4.1.2636.3.1.13.1.8',        'cpu_usage',        'performance', 'gauge',   '%',      1),
    (@juniper, '1.3.6.1.4.1.2636.3.1.13.1.11',       'mem_used_pct',     'performance', 'gauge',   '%',      1),
    (@juniper, '1.3.6.1.4.1.2636.3.1.13.1.7',         'temperature',     'performance', 'gauge',   'C',      1),
    (@juniper, '1.3.6.1.4.1.2636.3.40.1.4.1.1.1.3',   'vpn_tunnels',    'vpn',         'string',  NULL,     1);
END

PRINT 'seed_054b: 12 expanded SNMP vendor profiles seeded';

-- seed_073_cve_builtin.sql — Built-in CVE entries for common MSP software
-- High-impact CVEs that are frequently found in SMB environments
-- product_pattern uses SQL LIKE syntax (% wildcards)

-- Idempotent: skip if already seeded
IF EXISTS (SELECT 1 FROM cve_entries WHERE source = 'builtin') BEGIN
    PRINT 'CVE builtin entries already seeded, skipping';
    RETURN;
END

INSERT INTO cve_entries (cve_id, product_pattern, vendor, affected_below, fixed_version, severity, cvss_score, description, published_at, source)
VALUES
-- ── Chrome ──
('CVE-2024-7971', '%Google Chrome%', 'Google', '128.0.6613.85', '128.0.6613.85', 'critical', 8.8, 'Type confusion in V8 (actively exploited)', '2024-08-21', 'builtin'),
('CVE-2024-4671', '%Google Chrome%', 'Google', '124.0.6367.201', '124.0.6367.201', 'high', 8.8, 'Use after free in Visuals', '2024-05-09', 'builtin'),
('CVE-2024-5274', '%Google Chrome%', 'Google', '125.0.6422.112', '125.0.6422.112', 'high', 8.8, 'Type confusion in V8', '2024-05-23', 'builtin'),
('CVE-2025-0291', '%Google Chrome%', 'Google', '131.0.6778.265', '131.0.6778.265', 'high', 8.8, 'Type confusion in V8', '2025-01-08', 'builtin'),

-- ── Edge ──
('CVE-2024-49041', '%Microsoft Edge%', 'Microsoft', '131.0.2903.70', '131.0.2903.70', 'medium', 4.3, 'Spoofing vulnerability', '2024-12-06', 'builtin'),

-- ── Firefox ──
('CVE-2024-9680', '%Mozilla Firefox%', 'Mozilla', '131.0.2', '131.0.2', 'critical', 9.8, 'Use-after-free in Animation timelines (actively exploited)', '2024-10-09', 'builtin'),
('CVE-2025-0244', '%Mozilla Firefox%', 'Mozilla', '134.0', '134.0', 'high', 7.5, 'Address bar spoofing via redirect', '2025-01-07', 'builtin'),

-- ── 7-Zip ──
('CVE-2024-11477', '%7-Zip%', 'Igor Pavlov', '24.07', '24.07', 'high', 7.8, 'Integer underflow in Zstandard decompression allows RCE', '2024-11-22', 'builtin'),

-- ── WinRAR ──
('CVE-2023-38831', '%WinRAR%', 'RARLAB', '6.23', '6.23', 'critical', 7.8, 'Code execution when opening specially crafted archives (actively exploited)', '2023-08-23', 'builtin'),
('CVE-2024-36052', '%WinRAR%', 'RARLAB', '7.01', '7.01', 'medium', 5.3, 'Incorrect symlink validation', '2024-06-10', 'builtin'),

-- ── Adobe Acrobat / Reader ──
('CVE-2024-41869', '%Adobe Acrobat%', 'Adobe', '24.003.20054', '24.003.20054', 'critical', 7.8, 'Use after free allows code execution', '2024-09-10', 'builtin'),
('CVE-2024-41869', '%Adobe Reader%', 'Adobe', '24.003.20054', '24.003.20054', 'critical', 7.8, 'Use after free allows code execution', '2024-09-10', 'builtin'),
('CVE-2024-49530', '%Adobe Acrobat%', 'Adobe', '24.005.20307', '24.005.20307', 'high', 7.8, 'Use after free in font handling', '2024-12-10', 'builtin'),

-- ── Java / JRE / JDK ──
('CVE-2024-21235', '%Java%8 Update%', 'Oracle', '8 Update 421', '8 Update 421', 'medium', 4.8, 'Hotspot vulnerability', '2024-10-15', 'builtin'),
('CVE-2024-21210', '%Java%11%', 'Oracle', '11.0.25', '11.0.25', 'medium', 3.7, 'Serialization filter bypass', '2024-10-15', 'builtin'),

-- ── .NET Framework / Runtime ──
('CVE-2024-43498', '%.NET%8%', 'Microsoft', '8.0.11', '8.0.11', 'critical', 9.8, 'Remote code execution in NrbfDecoder', '2024-11-12', 'builtin'),
('CVE-2024-43499', '%.NET%8%', 'Microsoft', '8.0.11', '8.0.11', 'high', 7.5, 'Denial of service in string handling', '2024-11-12', 'builtin'),
('CVE-2025-21172', '%.NET%9%', 'Microsoft', '9.0.1', '9.0.1', 'high', 7.5, 'Remote code execution', '2025-01-14', 'builtin'),

-- ── Node.js ──
('CVE-2024-22020', '%Node.js%', 'Node.js', '20.15.1', '20.15.1', 'medium', 6.5, 'Bypass network import restriction via data URL', '2024-07-08', 'builtin'),
('CVE-2025-23083', '%Node.js%', 'Node.js', '22.13.1', '22.13.1', 'high', 7.7, 'Worker thread permission model bypass', '2025-01-22', 'builtin'),

-- ── Python ──
('CVE-2024-12254', '%Python 3%', 'Python', '3.12.8', '3.12.8', 'high', 7.5, 'Unbounded memory buffering in asyncio', '2024-12-06', 'builtin'),

-- ── VLC ──
('CVE-2024-46461', '%VLC%', 'VideoLAN', '3.0.21', '3.0.21', 'critical', 9.8, 'Integer overflow in MMS stream handling', '2024-09-12', 'builtin'),

-- ── PuTTY ──
('CVE-2024-31497', '%PuTTY%', 'Simon Tatham', '0.81', '0.81', 'critical', 9.8, 'ECDSA nonce bias allows private key recovery from 60 signatures', '2024-04-15', 'builtin'),

-- ── OpenSSH (via Git for Windows, etc.) ──
('CVE-2024-6387', '%OpenSSH%', 'OpenBSD', '9.8p1', '9.8p1', 'critical', 8.1, 'regreSSHion: signal handler race condition allows RCE', '2024-07-01', 'builtin'),

-- ── curl (bundled in many tools) ──
('CVE-2024-8096', '%curl%', 'curl project', '8.9.1', '8.9.1', 'medium', 5.3, 'OCSP stapling TLS certificate verification bypass', '2024-09-11', 'builtin'),

-- ── Zoom ──
('CVE-2024-39825', '%Zoom%', 'Zoom', '6.0.0', '6.0.0', 'critical', 8.5, 'Buffer overflow in Zoom Workplace client', '2024-07-09', 'builtin'),
('CVE-2024-45422', '%Zoom%', 'Zoom', '6.1.5', '6.1.5', 'medium', 6.5, 'Buffer overflow in installer', '2024-10-08', 'builtin'),

-- ── TeamViewer ──
('CVE-2024-7479', '%TeamViewer%', 'TeamViewer', '15.58.4', '15.58.4', 'high', 8.8, 'Improper signature verification allows privilege escalation', '2024-09-25', 'builtin'),
('CVE-2024-7481', '%TeamViewer%', 'TeamViewer', '15.58.4', '15.58.4', 'high', 8.8, 'Improper certificate verification in printer driver install', '2024-09-25', 'builtin'),

-- ── AnyDesk ──
('CVE-2024-12754', '%AnyDesk%', 'AnyDesk', '9.0.1', '9.0.1', 'high', 7.8, 'Local privilege escalation via background image handling', '2025-01-28', 'builtin'),

-- ── Cisco AnyConnect / Secure Client ──
('CVE-2024-20337', '%Cisco AnyConnect%', 'Cisco', '5.1.2.42', '5.1.2.42', 'high', 8.2, 'SAML token CRLF injection allows session hijacking', '2024-03-06', 'builtin'),
('CVE-2024-20337', '%Cisco Secure Client%', 'Cisco', '5.1.2.42', '5.1.2.42', 'high', 8.2, 'SAML token CRLF injection allows session hijacking', '2024-03-06', 'builtin'),

-- ── FortiClient VPN ──
('CVE-2024-36513', '%FortiClient%', 'Fortinet', '7.4.1', '7.4.1', 'high', 7.4, 'Privilege escalation via lua auto patch scripts', '2024-11-12', 'builtin'),

-- ── Veeam ──
('CVE-2024-40711', '%Veeam Backup%', 'Veeam', '12.2', '12.2', 'critical', 9.8, 'Unauthenticated RCE via deserialization', '2024-09-04', 'builtin'),

-- ── SolarWinds ──
('CVE-2024-28986', '%SolarWinds%', 'SolarWinds', '2024.2.1', '2024.2.1', 'critical', 9.8, 'Java deserialization RCE in Web Help Desk', '2024-08-13', 'builtin'),

-- ── Acronis ──
('CVE-2023-45249', '%Acronis%', 'Acronis', '15 Update 6', '15 Update 6', 'critical', 9.8, 'Default password RCE (actively exploited)', '2024-07-24', 'builtin'),

-- ── Ivanti / Pulse Secure ──
('CVE-2024-21887', '%Ivanti%Connect%', 'Ivanti', '22.5R2.3', '22.5R2.3', 'critical', 9.1, 'Command injection in web interface (actively exploited)', '2024-01-10', 'builtin'),
('CVE-2024-21887', '%Pulse Secure%', 'Ivanti', '22.5R2.3', '22.5R2.3', 'critical', 9.1, 'Command injection in web interface (actively exploited)', '2024-01-10', 'builtin'),

-- ── ConnectWise ScreenConnect ──
('CVE-2024-1709', '%ScreenConnect%', 'ConnectWise', '23.9.8', '23.9.8', 'critical', 10.0, 'Authentication bypass (actively exploited)', '2024-02-19', 'builtin'),
('CVE-2024-1709', '%ConnectWise Control%', 'ConnectWise', '23.9.8', '23.9.8', 'critical', 10.0, 'Authentication bypass (actively exploited)', '2024-02-19', 'builtin'),

-- ── Kaseya VSA ──
('CVE-2024-57726', '%Kaseya%', 'Kaseya', '9.5.0.46', '9.5.0.46', 'high', 8.4, 'Missing authorization allows privilege escalation', '2025-02-03', 'builtin'),

-- ── Microsoft Office / 365 Apps ──
('CVE-2024-49040', '%Microsoft Office%', 'Microsoft', '16.0.18025', '16.0.18025', 'high', 7.5, 'Spoofing in Outlook email sender display', '2024-11-12', 'builtin'),
('CVE-2025-21298', '%Microsoft Office%', 'Microsoft', '16.0.18227', '16.0.18227', 'critical', 9.8, 'Windows OLE RCE via specially crafted email', '2025-01-14', 'builtin'),

-- ── Microsoft Visual C++ Redistributable ──
('CVE-2024-43590', '%Visual C++ Redistributable%', 'Microsoft', '14.40.33816', '14.40.33816', 'high', 7.8, 'Elevation of privilege', '2024-10-08', 'builtin'),

-- ── Notepad++ ──
('CVE-2023-40031', '%Notepad++%', 'Notepad++', '8.5.7', '8.5.7', 'high', 7.8, 'Heap buffer overflow in UTF-16 handling', '2023-08-25', 'builtin'),

-- ── FileZilla ──
('CVE-2024-46981', '%FileZilla%', 'FileZilla', '3.67.1', '3.67.1', 'medium', 5.9, 'Improper TLS certificate validation', '2024-12-18', 'builtin'),

-- ── LibreOffice ──
('CVE-2024-7788', '%LibreOffice%', 'TDF', '24.8.0', '24.8.0', 'high', 7.8, 'Signature verification bypass allows macro execution', '2024-09-17', 'builtin'),

-- ── Git for Windows ──
('CVE-2024-32002', '%Git%for Windows%', 'Git', '2.45.1', '2.45.1', 'critical', 9.0, 'Recursive clone RCE via crafted submodules', '2024-05-14', 'builtin'),

-- ── KeePass ──
('CVE-2023-32784', '%KeePass%', 'KeePass', '2.54', '2.54', 'high', 7.5, 'Master password recovery from process memory', '2023-05-15', 'builtin'),

-- ── Wireshark ──
('CVE-2024-9781', '%Wireshark%', 'Wireshark', '4.4.1', '4.4.1', 'medium', 5.5, 'AppleTalk dissector crash', '2024-10-09', 'builtin'),

-- ── GIMP ──
('CVE-2023-44444', '%GIMP%', 'GIMP', '2.10.36', '2.10.36', 'high', 7.8, 'Buffer overflow in PSD file parsing allows RCE', '2024-01-09', 'builtin'),

-- ── SQL Server Management Studio ──
('CVE-2024-49043', '%SQL Server Management Studio%', 'Microsoft', '20.2', '20.2', 'high', 7.8, 'RCE in OLE DB provider', '2024-11-12', 'builtin'),

-- ── VMware Workstation ──
('CVE-2024-38812', '%VMware Workstation%', 'VMware', '17.6', '17.6', 'critical', 9.8, 'Heap overflow in DCE/RPC allows RCE', '2024-09-17', 'builtin'),

-- ── Docker Desktop ──
('CVE-2024-8695', '%Docker Desktop%', 'Docker', '4.34.2', '4.34.2', 'critical', 9.8, 'RCE via crafted extension description', '2024-09-12', 'builtin');

PRINT 'Seeded ' + CAST(@@ROWCOUNT AS VARCHAR) + ' builtin CVE entries';
GO

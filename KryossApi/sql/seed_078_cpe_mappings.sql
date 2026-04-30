-- seed_078_cpe_mappings.sql
-- Maps software.name patterns to NVD CPE vendor:product pairs
-- Idempotent: only updates rows where cpe_vendor IS NULL

UPDATE software SET cpe_vendor = 'google', cpe_product = 'chrome' WHERE name LIKE '%Google Chrome%' AND cpe_vendor IS NULL;
UPDATE software SET cpe_vendor = 'mozilla', cpe_product = 'firefox' WHERE name LIKE '%Mozilla Firefox%' AND cpe_vendor IS NULL;
UPDATE software SET cpe_vendor = 'microsoft', cpe_product = 'edge_chromium' WHERE name LIKE '%Microsoft Edge%' AND cpe_vendor IS NULL;
UPDATE software SET cpe_vendor = 'microsoft', cpe_product = 'office' WHERE name LIKE '%Microsoft Office%' AND cpe_vendor IS NULL;
UPDATE software SET cpe_vendor = 'microsoft', cpe_product = '365_apps' WHERE name LIKE '%Microsoft 365%' AND cpe_vendor IS NULL;
UPDATE software SET cpe_vendor = 'microsoft', cpe_product = 'visual_studio_code' WHERE name LIKE '%Visual Studio Code%' AND cpe_vendor IS NULL;
UPDATE software SET cpe_vendor = 'microsoft', cpe_product = '.net_framework' WHERE name LIKE '%.NET%Runtime%' AND cpe_vendor IS NULL;
UPDATE software SET cpe_vendor = 'adobe', cpe_product = 'acrobat_reader_dc' WHERE (name LIKE '%Adobe Acrobat%' OR name LIKE '%Adobe Reader%') AND cpe_vendor IS NULL;
UPDATE software SET cpe_vendor = 'oracle', cpe_product = 'jre' WHERE name LIKE '%Java%Runtime%' AND cpe_vendor IS NULL;
UPDATE software SET cpe_vendor = 'igor_pavlov', cpe_product = '7-zip' WHERE name LIKE '%7-Zip%' AND cpe_vendor IS NULL;
UPDATE software SET cpe_vendor = 'rarlab', cpe_product = 'winrar' WHERE name LIKE '%WinRAR%' AND cpe_vendor IS NULL;
UPDATE software SET cpe_vendor = 'zoom', cpe_product = 'zoom' WHERE name LIKE '%Zoom%' AND cpe_vendor IS NULL;
UPDATE software SET cpe_vendor = 'teamviewer', cpe_product = 'teamviewer' WHERE name LIKE '%TeamViewer%' AND cpe_vendor IS NULL;
UPDATE software SET cpe_vendor = 'anydesk', cpe_product = 'anydesk' WHERE name LIKE '%AnyDesk%' AND cpe_vendor IS NULL;
UPDATE software SET cpe_vendor = 'cisco', cpe_product = 'anyconnect_secure_mobility_client' WHERE (name LIKE '%Cisco AnyConnect%' OR name LIKE '%Cisco Secure Client%') AND cpe_vendor IS NULL;
UPDATE software SET cpe_vendor = 'fortinet', cpe_product = 'forticlient' WHERE name LIKE '%FortiClient%' AND cpe_vendor IS NULL;
UPDATE software SET cpe_vendor = 'veeam', cpe_product = 'backup_and_replication' WHERE name LIKE '%Veeam%' AND cpe_vendor IS NULL;
UPDATE software SET cpe_vendor = 'connectwise', cpe_product = 'screenconnect' WHERE (name LIKE '%ScreenConnect%' OR name LIKE '%ConnectWise Control%') AND cpe_vendor IS NULL;
UPDATE software SET cpe_vendor = 'putty', cpe_product = 'putty' WHERE name LIKE '%PuTTY%' AND cpe_vendor IS NULL;
UPDATE software SET cpe_vendor = 'videolan', cpe_product = 'vlc_media_player' WHERE name LIKE '%VLC%' AND cpe_vendor IS NULL;
UPDATE software SET cpe_vendor = 'notepad-plus-plus', cpe_product = 'notepad\+\+' WHERE name LIKE '%Notepad++%' AND cpe_vendor IS NULL;
UPDATE software SET cpe_vendor = 'filezilla-project', cpe_product = 'filezilla_client' WHERE name LIKE '%FileZilla%' AND cpe_vendor IS NULL;
UPDATE software SET cpe_vendor = 'libreoffice', cpe_product = 'libreoffice' WHERE name LIKE '%LibreOffice%' AND cpe_vendor IS NULL;
UPDATE software SET cpe_vendor = 'git-scm', cpe_product = 'git' WHERE name LIKE '%Git for Windows%' AND cpe_vendor IS NULL;
UPDATE software SET cpe_vendor = 'keepass', cpe_product = 'keepass' WHERE name LIKE '%KeePass%' AND cpe_vendor IS NULL;
UPDATE software SET cpe_vendor = 'wireshark', cpe_product = 'wireshark' WHERE name LIKE '%Wireshark%' AND cpe_vendor IS NULL;
UPDATE software SET cpe_vendor = 'vmware', cpe_product = 'workstation' WHERE name LIKE '%VMware Workstation%' AND cpe_vendor IS NULL;
UPDATE software SET cpe_vendor = 'docker', cpe_product = 'docker_desktop' WHERE name LIKE '%Docker Desktop%' AND cpe_vendor IS NULL;
UPDATE software SET cpe_vendor = 'nodejs', cpe_product = 'node.js' WHERE name LIKE '%Node.js%' AND cpe_vendor IS NULL;
UPDATE software SET cpe_vendor = 'python', cpe_product = 'python' WHERE name LIKE '%Python%' AND cpe_vendor IS NULL;
UPDATE software SET cpe_vendor = 'solarwinds', cpe_product = 'orion_platform' WHERE name LIKE '%SolarWinds%' AND cpe_vendor IS NULL;
UPDATE software SET cpe_vendor = 'acronis', cpe_product = 'cyber_protect' WHERE name LIKE '%Acronis%' AND cpe_vendor IS NULL;
UPDATE software SET cpe_vendor = 'ivanti', cpe_product = 'connect_secure' WHERE (name LIKE '%Ivanti%Connect%' OR name LIKE '%Pulse Secure%') AND cpe_vendor IS NULL;
UPDATE software SET cpe_vendor = 'apple', cpe_product = 'itunes' WHERE name LIKE '%iTunes%' AND cpe_vendor IS NULL;
UPDATE software SET cpe_vendor = 'apple', cpe_product = 'icloud' WHERE name LIKE '%iCloud%' AND cpe_vendor IS NULL;
UPDATE software SET cpe_vendor = 'slack', cpe_product = 'slack' WHERE name LIKE '%Slack%' AND cpe_vendor IS NULL;
UPDATE software SET cpe_vendor = 'dropbox', cpe_product = 'dropbox' WHERE name LIKE '%Dropbox%' AND cpe_vendor IS NULL;

UPDATE software SET is_commercial = 1 WHERE cpe_vendor IN ('microsoft', 'adobe', 'teamviewer', 'zoom', 'veeam', 'acronis', 'vmware', 'fortinet', 'cisco', 'solarwinds', 'connectwise', 'ivanti', 'apple', 'slack', 'dropbox');

PRINT 'CPE mappings applied';
GO

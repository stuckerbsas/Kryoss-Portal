namespace KryossAgent.Services;

public static class OuiLookup
{
    public static (string Vendor, string Category)? Lookup(string mac)
    {
        if (string.IsNullOrEmpty(mac) || mac.Length < 8) return null;
        var prefix = mac[..8].ToUpperInvariant().Replace('-', ':');
        return Db.GetValueOrDefault(prefix);
    }

    // Category: network, wireless, firewall, printer, computer, phone,
    //           camera, iot, nas, ups, audio, virtual, unknown
    private static readonly Dictionary<string, (string Vendor, string Category)> Db = new()
    {
        // ── Cisco ──
        {"00:1A:A1", ("Cisco", "network")}, {"00:1E:49", ("Cisco", "network")},
        {"00:50:0F", ("Cisco", "network")}, {"58:8D:09", ("Cisco", "network")},
        {"AC:F2:C5", ("Cisco", "network")}, {"F4:CF:E2", ("Cisco", "network")},
        {"D4:6D:50", ("Cisco", "network")}, {"0C:75:BD", ("Cisco", "network")},
        {"00:22:55", ("Cisco", "network")}, {"00:23:04", ("Cisco", "network")},
        {"68:7F:74", ("Cisco", "network")}, {"44:AD:D9", ("Cisco", "network")},
        {"B0:7D:47", ("Cisco", "network")}, {"70:DB:98", ("Cisco", "network")},
        {"F8:7B:20", ("Cisco", "network")},

        // ── Meraki (Cisco) ──
        {"0C:8D:DB", ("Meraki", "wireless")}, {"68:3A:1E", ("Meraki", "wireless")},
        {"E8:26:89", ("Meraki", "wireless")}, {"AC:17:C8", ("Meraki", "wireless")},

        // ── Juniper ──
        {"00:05:85", ("Juniper", "network")}, {"00:10:DB", ("Juniper", "network")},
        {"88:E0:F3", ("Juniper", "network")}, {"54:1E:56", ("Juniper", "network")},
        {"28:8A:1C", ("Juniper", "network")}, {"F0:1C:2D", ("Juniper", "network")},

        // ── HP / HPE ──
        {"00:17:A4", ("HP", "computer")}, {"00:1A:4B", ("HP", "computer")},
        {"10:1F:74", ("HP", "computer")}, {"3C:D9:2B", ("HP", "computer")},
        {"48:0F:CF", ("HP", "computer")}, {"94:57:A5", ("HP", "computer")},
        {"D0:BF:9C", ("HP", "computer")}, {"80:CE:62", ("HP", "computer")},
        {"00:21:5A", ("HP", "computer")}, {"18:64:72", ("HP", "computer")},
        {"9C:B6:54", ("HP", "computer")}, {"B4:B5:2F", ("HP", "computer")},
        {"E4:11:5B", ("HP", "computer")}, {"A0:D3:C1", ("HP", "computer")},

        // ── Aruba ──
        {"00:0B:86", ("Aruba", "wireless")}, {"00:24:6C", ("Aruba", "wireless")},
        {"04:BD:88", ("Aruba", "wireless")}, {"20:4C:03", ("Aruba", "wireless")},
        {"24:DE:C6", ("Aruba", "wireless")}, {"40:E3:D6", ("Aruba", "wireless")},
        {"6C:F3:7F", ("Aruba", "wireless")}, {"94:B4:0F", ("Aruba", "wireless")},
        {"D8:C7:C8", ("Aruba", "wireless")},

        // ── Ubiquiti ──
        {"24:5A:4C", ("Ubiquiti", "wireless")}, {"44:D9:E7", ("Ubiquiti", "wireless")},
        {"68:72:51", ("Ubiquiti", "wireless")}, {"78:8A:20", ("Ubiquiti", "wireless")},
        {"80:2A:A8", ("Ubiquiti", "wireless")}, {"DC:9F:DB", ("Ubiquiti", "wireless")},
        {"FC:EC:DA", ("Ubiquiti", "wireless")}, {"B4:FB:E4", ("Ubiquiti", "wireless")},
        {"04:18:D6", ("Ubiquiti", "wireless")}, {"74:83:C2", ("Ubiquiti", "wireless")},
        {"E0:63:DA", ("Ubiquiti", "wireless")}, {"F0:9F:C2", ("Ubiquiti", "wireless")},

        // ── Ruckus ──
        {"00:22:7F", ("Ruckus", "wireless")}, {"04:4B:FF", ("Ruckus", "wireless")},
        {"74:91:1A", ("Ruckus", "wireless")}, {"84:18:3A", ("Ruckus", "wireless")},

        // ── Fortinet ──
        {"00:09:0F", ("Fortinet", "firewall")}, {"08:5B:0E", ("Fortinet", "firewall")},
        {"70:4C:A5", ("Fortinet", "firewall")}, {"90:6C:AC", ("Fortinet", "firewall")},

        // ── SonicWall ──
        {"00:06:B1", ("SonicWall", "firewall")}, {"00:17:C5", ("SonicWall", "firewall")},
        {"C0:EA:E4", ("SonicWall", "firewall")},

        // ── Palo Alto ──
        {"00:1B:17", ("Palo Alto", "firewall")}, {"00:86:9C", ("Palo Alto", "firewall")},
        {"08:30:6B", ("Palo Alto", "firewall")}, {"B4:0C:25", ("Palo Alto", "firewall")},

        // ── WatchGuard ──
        {"00:90:7F", ("WatchGuard", "firewall")},

        // ── MikroTik ──
        {"00:0C:42", ("MikroTik", "network")}, {"48:8F:5A", ("MikroTik", "network")},
        {"64:D1:54", ("MikroTik", "network")}, {"74:4D:28", ("MikroTik", "network")},
        {"CC:2D:E0", ("MikroTik", "network")}, {"E4:8D:8C", ("MikroTik", "network")},

        // ── TP-Link ──
        {"14:EB:B6", ("TP-Link", "network")}, {"30:DE:4B", ("TP-Link", "network")},
        {"50:C7:BF", ("TP-Link", "network")}, {"60:A4:B7", ("TP-Link", "network")},
        {"98:DA:C4", ("TP-Link", "network")}, {"B0:95:75", ("TP-Link", "network")},
        {"C0:06:C3", ("TP-Link", "network")}, {"EC:08:6B", ("TP-Link", "network")},

        // ── Netgear ──
        {"00:14:6C", ("Netgear", "network")}, {"00:1E:2A", ("Netgear", "network")},
        {"20:E5:2A", ("Netgear", "network")}, {"44:94:FC", ("Netgear", "network")},
        {"6C:B0:CE", ("Netgear", "network")}, {"C4:04:15", ("Netgear", "network")},

        // ── Dell ──
        {"00:14:22", ("Dell", "computer")}, {"18:A9:9B", ("Dell", "computer")},
        {"34:17:EB", ("Dell", "computer")}, {"54:9F:35", ("Dell", "computer")},
        {"B0:83:FE", ("Dell", "computer")}, {"D4:AE:52", ("Dell", "computer")},
        {"F4:8E:38", ("Dell", "computer")}, {"F8:DB:88", ("Dell", "computer")},
        {"14:18:77", ("Dell", "computer")}, {"98:90:96", ("Dell", "computer")},
        {"50:9A:4C", ("Dell", "computer")}, {"24:B6:FD", ("Dell", "computer")},

        // ── Lenovo ──
        {"54:EE:75", ("Lenovo", "computer")}, {"74:E5:43", ("Lenovo", "computer")},
        {"E8:6A:64", ("Lenovo", "computer")}, {"28:D2:44", ("Lenovo", "computer")},
        {"98:FA:9B", ("Lenovo", "computer")}, {"50:5B:C2", ("Lenovo", "computer")},
        {"8C:16:45", ("Lenovo", "computer")}, {"C8:5B:76", ("Lenovo", "computer")},

        // ── Intel ──
        {"00:1E:64", ("Intel", "computer")}, {"3C:97:0E", ("Intel", "computer")},
        {"68:05:CA", ("Intel", "computer")}, {"A0:36:9F", ("Intel", "computer")},
        {"B4:96:91", ("Intel", "computer")}, {"8C:EC:4B", ("Intel", "computer")},

        // ── Apple ──
        {"3C:22:FB", ("Apple", "phone")}, {"7C:D1:C3", ("Apple", "phone")},
        {"A8:51:5B", ("Apple", "phone")}, {"BC:54:36", ("Apple", "phone")},
        {"DC:A4:CA", ("Apple", "phone")}, {"F0:18:98", ("Apple", "phone")},
        {"14:99:E2", ("Apple", "phone")}, {"20:9B:CD", ("Apple", "phone")},
        {"38:C9:86", ("Apple", "phone")}, {"60:FA:CD", ("Apple", "phone")},
        {"8C:85:90", ("Apple", "phone")}, {"A4:83:E7", ("Apple", "phone")},
        {"C8:69:CD", ("Apple", "phone")}, {"E0:5F:45", ("Apple", "phone")},
        {"AC:BC:32", ("Apple", "phone")}, {"D0:03:4B", ("Apple", "phone")},
        {"F4:5C:89", ("Apple", "phone")}, {"78:7E:61", ("Apple", "phone")},
        {"B8:E8:56", ("Apple", "phone")}, {"40:B3:95", ("Apple", "phone")},
        {"88:66:A5", ("Apple", "phone")}, {"64:A3:CB", ("Apple", "phone")},
        {"28:6A:BA", ("Apple", "phone")},

        // ── Samsung ──
        {"00:15:99", ("Samsung", "phone")}, {"08:D4:2B", ("Samsung", "phone")},
        {"28:CC:01", ("Samsung", "phone")}, {"50:01:BB", ("Samsung", "phone")},
        {"78:52:1A", ("Samsung", "phone")}, {"8C:F5:A3", ("Samsung", "phone")},
        {"A4:08:EA", ("Samsung", "phone")}, {"CC:07:AB", ("Samsung", "phone")},
        {"E4:7C:F9", ("Samsung", "phone")}, {"94:35:0A", ("Samsung", "phone")},
        {"30:07:4D", ("Samsung", "phone")}, {"B4:CE:F6", ("Samsung", "phone")},
        {"D0:87:E2", ("Samsung", "phone")}, {"84:D3:2A", ("Samsung", "phone")},
        {"10:D5:42", ("Samsung", "phone")}, {"FC:F1:36", ("Samsung", "phone")},

        // ── Google ──
        {"08:9E:08", ("Google", "iot")}, {"14:C1:4E", ("Google", "iot")},
        {"30:FD:38", ("Google", "iot")}, {"54:60:09", ("Google", "iot")},
        {"A4:77:33", ("Google", "iot")}, {"F4:F5:D8", ("Google", "iot")},

        // ── Amazon ──
        {"0C:47:C9", ("Amazon", "iot")}, {"18:74:2E", ("Amazon", "iot")},
        {"40:A2:DB", ("Amazon", "iot")}, {"44:65:0D", ("Amazon", "iot")},
        {"68:54:FD", ("Amazon", "iot")}, {"84:D6:D0", ("Amazon", "iot")},
        {"FC:65:DE", ("Amazon", "iot")}, {"F0:F0:A4", ("Amazon", "iot")},

        // ── VMware ──
        {"00:05:69", ("VMware", "virtual")}, {"00:0C:29", ("VMware", "virtual")},
        {"00:50:56", ("VMware", "virtual")},

        // ── Microsoft / Hyper-V ──
        {"00:15:5D", ("Hyper-V", "virtual")}, {"28:18:78", ("Microsoft", "computer")},

        // ── Printers ──
        {"00:80:77", ("Brother", "printer")}, {"00:1B:A9", ("Brother", "printer")},
        {"30:05:5C", ("Brother", "printer")},
        {"00:00:48", ("Epson", "printer")}, {"00:26:AB", ("Epson", "printer")},
        {"64:EB:8C", ("Epson", "printer")},
        {"00:1E:8F", ("Canon", "printer")}, {"00:BB:C1", ("Canon", "printer")},
        {"18:0C:AC", ("Canon", "printer")}, {"2C:9E:FC", ("Canon", "printer")},
        {"00:00:AA", ("Xerox", "printer")}, {"9C:93:4E", ("Xerox", "printer")},
        {"64:00:F1", ("Xerox", "printer")},
        {"00:04:00", ("Lexmark", "printer")}, {"00:20:00", ("Lexmark", "printer")},
        {"00:80:45", ("Konica Minolta", "printer")},
        {"00:00:74", ("Ricoh", "printer")}, {"00:26:73", ("Ricoh", "printer")},
        {"00:A0:F8", ("Zebra", "printer")}, {"00:07:4D", ("Zebra", "printer")},

        // ── Cameras ──
        {"28:57:BE", ("Hikvision", "camera")}, {"44:19:B6", ("Hikvision", "camera")},
        {"54:C4:15", ("Hikvision", "camera")}, {"C0:56:E3", ("Hikvision", "camera")},
        {"3C:EF:8C", ("Dahua", "camera")}, {"90:02:A9", ("Dahua", "camera")},
        {"A0:BD:1D", ("Dahua", "camera")}, {"E0:50:8B", ("Dahua", "camera")},
        {"00:40:8C", ("Axis", "camera")}, {"AC:CC:8E", ("Axis", "camera")},
        {"B8:A4:4F", ("Axis", "camera")},

        // ── NAS ──
        {"00:11:32", ("Synology", "nas")},
        {"00:08:9B", ("QNAP", "nas")}, {"24:5E:BE", ("QNAP", "nas")},

        // ── UPS ──
        {"00:C0:B7", ("APC", "ups")}, {"00:06:4F", ("APC", "ups")},
        {"00:20:85", ("Eaton", "ups")},
        {"00:06:67", ("CyberPower", "ups")},

        // ── Audio / IoT ──
        {"00:0E:58", ("Sonos", "audio")}, {"34:7E:5C", ("Sonos", "audio")},
        {"48:A6:B8", ("Sonos", "audio")}, {"78:28:CA", ("Sonos", "audio")},
        {"B8:27:EB", ("Raspberry Pi", "iot")}, {"DC:A6:32", ("Raspberry Pi", "iot")},
        {"E4:5F:01", ("Raspberry Pi", "iot")}, {"28:CD:C1", ("Raspberry Pi", "iot")},
        {"10:59:32", ("Roku", "iot")}, {"B0:A7:37", ("Roku", "iot")},

        // ── Phones (VoIP) ──
        {"00:04:F2", ("Polycom", "phone")}, {"00:E0:DB", ("Polycom", "phone")},
        {"64:16:7F", ("Polycom", "phone")},
        {"80:5E:C0", ("Yealink", "phone")}, {"80:5E:0C", ("Yealink", "phone")},

        // ── Realtek (common on-board NICs) ──
        {"00:E0:4C", ("Realtek", "computer")}, {"52:54:00", ("Realtek", "computer")},

        // ── ASUS ──
        {"00:1A:92", ("ASUS", "computer")}, {"04:D4:C4", ("ASUS", "computer")},
        {"2C:FD:A1", ("ASUS", "computer")}, {"AC:22:0B", ("ASUS", "computer")},

        // ── Motorola/Zebra ──
        {"00:0B:06", ("Motorola", "phone")}, {"00:1A:66", ("Motorola", "phone")},
    };
}

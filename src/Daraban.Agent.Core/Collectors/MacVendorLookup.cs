namespace Daraban.Agent.Core.Collectors;

/// <summary>
/// When neither reverse DNS nor mDNS gives you a name at all, the MAC's OUI (first 3 bytes)
/// can still identify the manufacturer — but only if the MAC is a real factory-assigned
/// address. Modern OSes (Windows 10 1903+, Android 10+, iOS 14+) generate a random
/// "private"/"locally administered" MAC per network by default specifically to prevent this
/// kind of lookup, so a null result there is not a gap in the OUI table — it's by design, and
/// no OUI table, however complete, can ever resolve it.
///
/// For real (non-randomized) MACs, this ships with a small hand-picked table. For real
/// coverage, download the full IEEE OUI registry (https://standards-oui.ieee.org/oui/oui.csv,
/// ~30k entries) and replace/extend this dictionary at startup — the lookup key stays the
/// same (first 6 hex chars of the MAC, no separators, upper-case).
/// </summary>
public static class MacVendorLookup
{
    private static readonly Dictionary<string, string> KnownOuis = new()
    {
        ["001A11"] = "Google",
        ["3C5AB4"] = "Google",
        ["F4F5D8"] = "Google",
        ["A48830"] = "Apple",
        ["F0DBF8"] = "Apple",
        ["D4619D"] = "Apple",
        ["001EC2"] = "Apple",
        ["8863DF"] = "Apple",
        ["ACBC32"] = "Apple",
        ["CC29F5"] = "Samsung",
        ["1CC1DE"] = "Samsung",
        ["8425DB"] = "Samsung",
        ["5C0A5B"] = "Xiaomi",
        ["286C07"] = "Xiaomi",
        ["9C29FD"] = "Xiaomi",
        ["EC5C68"] = "Xiaomi",
        ["ECFABC"] = "Espressif (ESP8266/ESP32 — smart plugs, sensors, cheap IoT)",
        ["246F28"] = "Espressif (ESP8266/ESP32 — smart plugs, sensors, cheap IoT)",
        ["3C71BF"] = "Espressif (ESP8266/ESP32 — smart plugs, sensors, cheap IoT)",
        ["B827EB"] = "Raspberry Pi Foundation",
        ["DCA632"] = "Raspberry Pi Foundation",
        ["E45F01"] = "Raspberry Pi Foundation",
        ["00E04C"] = "Realtek (generic WiFi/Ethernet chipset — routers, adapters)",
        ["001DD8"] = "Microsoft",
        ["7CED8D"] = "Microsoft (Surface/Xbox)",
        ["3C2AF4"] = "TP-Link",
        ["50C7BF"] = "TP-Link",
        ["A0F3C1"] = "D-Link",
        // NOTE: these entries are illustrative examples, not individually verified against
        // the live IEEE registry — swap in the real oui.csv before relying on this for
        // anything production-grade. E.g. A0:9F:7A (a real router OUI seen in testing) is
        // NOT in this demo table — that's the table being incomplete, a different situation
        // from IsLikelyRandomized(...) == true below, which is unresolvable no matter how
        // complete the table is.
    };

    /// <summary>
    /// True if the MAC's "locally administered" bit is set — i.e. it was generated for
    /// privacy (Windows/Android/iOS per-network random MAC), not assigned by a manufacturer.
    /// When this is true, NO OUI table — however complete — can ever produce a real vendor,
    /// because there is no real vendor encoded in the address at all.
    /// </summary>
    public static bool IsLikelyRandomized(string? macAddress)
    {
        var firstByte = FirstOctet(macAddress);
        if (firstByte is null) return false;
        return (firstByte.Value & 0x02) != 0;
    }

    /// <summary>
    /// Returns a vendor/product-family guess from the MAC's OUI, or null if either the MAC
    /// is randomized/private (unresolvable by design) or it's a real OUI this table doesn't
    /// know yet. Use IsLikelyRandomized separately if you need to tell those two cases apart
    /// in the UI (e.g. "Vendor unknown" vs. "Private/randomized address").
    /// </summary>
    public static string? Lookup(string? macAddress)
    {
        if (string.IsNullOrWhiteSpace(macAddress)) return null;
        if (IsLikelyRandomized(macAddress)) return null;

        var oui = macAddress.Replace(":", "").Replace("-", "").ToUpperInvariant();
        if (oui.Length < 6) return null;
        oui = oui[..6];

        return KnownOuis.GetValueOrDefault(oui);
    }

    private static byte? FirstOctet(string? macAddress)
    {
        if (string.IsNullOrWhiteSpace(macAddress)) return null;
        var cleaned = macAddress.Replace(":", "").Replace("-", "");
        if (cleaned.Length < 2) return null;
        return Convert.ToByte(cleaned[..2], 16);
    }
}
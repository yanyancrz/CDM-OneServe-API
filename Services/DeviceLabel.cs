namespace CDM_OneServe_API.Services;

/// <summary>
/// Turns a User-Agent into something a person recognises, so the dashboard
/// can say "Chrome on Android" instead of dumping 200 characters of string.
/// </summary>
public static class DeviceLabel
{
    public static string? FromUserAgent(string? userAgent)
    {
        var ua = (userAgent ?? string.Empty).Trim();
        if (ua.Length == 0) return null;

        // ---- browser, most specific first ----
        var browser =
            ua.Contains("Edg/", StringComparison.OrdinalIgnoreCase) ? "Edge"
            : ua.Contains("OPR/", StringComparison.OrdinalIgnoreCase) ? "Opera"
            : ua.Contains("SamsungBrowser", StringComparison.OrdinalIgnoreCase) ? "Samsung Internet"
            : ua.Contains("Firefox/", StringComparison.OrdinalIgnoreCase) ? "Firefox"
            : ua.Contains("CriOS", StringComparison.OrdinalIgnoreCase) ? "Chrome on iOS"
            : ua.Contains("FxiOS", StringComparison.OrdinalIgnoreCase) ? "Firefox on iOS"
            : ua.Contains("Chrome/", StringComparison.OrdinalIgnoreCase) ? "Chrome"
            : ua.Contains("Safari/", StringComparison.OrdinalIgnoreCase) ? "Safari"
            : "this browser";

        // ---- OS, same order ----
        var os =
            ua.Contains("Android", StringComparison.OrdinalIgnoreCase) ? "Android"
            : ua.Contains("iPhone", StringComparison.OrdinalIgnoreCase) ||
              ua.Contains("iPad", StringComparison.OrdinalIgnoreCase) ? "iOS"
            : ua.Contains("Windows", StringComparison.OrdinalIgnoreCase) ? "Windows"
            : ua.Contains("Mac OS", StringComparison.OrdinalIgnoreCase) ? "macOS"
            : ua.Contains("CrOS", StringComparison.OrdinalIgnoreCase) ? "ChromeOS"
            : ua.Contains("Linux", StringComparison.OrdinalIgnoreCase) ? "Linux"
            : null;

        return os is null ? browser : $"{browser} on {os}";
    }
}

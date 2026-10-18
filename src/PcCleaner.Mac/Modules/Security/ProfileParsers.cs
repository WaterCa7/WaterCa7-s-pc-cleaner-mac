using System.Text.Json;

namespace PcCleaner.Modules.Security;

public enum PayloadKind
{
    Other,

    /// <summary>Device management by an organization (com.apple.mdm).</summary>
    Mdm,

    /// <summary>Sends traffic through a proxy (com.apple.proxy…).</summary>
    Proxy,

    /// <summary>Installs a root certificate: lets its owner read encrypted traffic.</summary>
    RootCertificate,

    /// <summary>Settings of a browser (home page, search engine, extensions).</summary>
    Browser,
}

/// <summary>A configuration profile and what its payloads do.</summary>
public sealed record ConfigProfile(string Name, string? Organization, IReadOnlyList<string> Payloads)
{
    public IReadOnlyList<PayloadKind> Kinds => Payloads.Select(ProfileParsers.Classify).Distinct().ToList();

    public bool IsMdm => Kinds.Contains(PayloadKind.Mdm);

    /// <summary>Changes the browser, the proxy or trusts a root certificate, outside of device management.</summary>
    public bool IsRisky => !IsMdm && Kinds.Any(k => k is PayloadKind.Proxy or PayloadKind.RootCertificate or PayloadKind.Browser);

    public string WhatText
    {
        get
        {
            var parts = new List<string>();
            if (IsMdm)
                parts.Add("управление Mac организацией (MDM)");
            if (Kinds.Contains(PayloadKind.Proxy))
                parts.Add("прокси");
            if (Kinds.Contains(PayloadKind.RootCertificate))
                parts.Add("корневой сертификат");
            if (Kinds.Contains(PayloadKind.Browser))
                parts.Add("настройки браузера");
            return parts.Count == 0 ? "настройки macOS" : string.Join(", ", parts);
        }
    }
}

/// <summary>`system_profiler SPConfigurationProfileDataType -json`, read tolerantly: profiles are the items with profile fields.</summary>
public static class ProfileParsers
{
    private static readonly string[] BrowserIds =
    {
        "com.google.chrome", "com.apple.safari", "org.mozilla.firefox", "com.microsoft.edge", "com.brave.browser", "com.operasoftware",
        "ru.yandex.desktop.yandex-browser", "com.apple.managedclient.preferences",
    };

    public static IReadOnlyList<ConfigProfile> Parse(string json)
    {
        var result = new List<ConfigProfile>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            Walk(doc.RootElement, result);
        }
        catch (JsonException)
        {
            // Not JSON: no profiles could be read.
        }
        return result;
    }

    public static PayloadKind Classify(string payloadType)
    {
        var t = payloadType.ToLowerInvariant();
        if (t == "com.apple.mdm" || t.StartsWith("com.apple.mdm.", StringComparison.Ordinal))
            return PayloadKind.Mdm;
        if (t.Contains("proxy", StringComparison.Ordinal))
            return PayloadKind.Proxy;
        if (t is "com.apple.security.root" or "com.apple.security.pem" or "com.apple.security.pkcs1")
            return PayloadKind.RootCertificate;
        if (BrowserIds.Any(b => t.StartsWith(b, StringComparison.Ordinal)))
            return PayloadKind.Browser;
        return PayloadKind.Other;
    }

    private static void Walk(JsonElement element, List<ConfigProfile> result)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
                Walk(item, result);
            return;
        }
        if (element.ValueKind != JsonValueKind.Object)
            return;

        if (IsProfile(element))
        {
            var payloads = new List<string>();
            if (element.TryGetProperty("_items", out var items) && items.ValueKind == JsonValueKind.Array)
            {
                foreach (var payload in items.EnumerateArray())
                {
                    if (String(payload, "_name") is { } type)
                        payloads.Add(type);
                }
            }
            result.Add(new ConfigProfile(String(element, "_name") ?? "Без названия", String(element, "spconfigprofile_organization"), payloads));
            return;
        }
        foreach (var property in element.EnumerateObject())
            Walk(property.Value, result);
    }

    private static bool IsProfile(JsonElement element)
        => element.EnumerateObject().Any(p => p.Name is "spconfigprofile_profile_identifier" or "spconfigprofile_organization" or "spconfigprofile_install_date");

    private static string? String(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}

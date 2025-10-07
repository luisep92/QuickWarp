using System;
using System.IO;
using BepInEx;
using UnityEngine;

namespace QuickWarp.Configuration;

// KeyCode docs https://docs.unity3d.com/6000.2/Documentation/ScriptReference/KeyCode.html
internal sealed class ModConfiguration
{
    public KeyCode SaveWarpKey { get; private set; } = KeyCode.F6;

    public KeyCode LoadWarpKey { get; private set; } = KeyCode.F7;

    private readonly string _configFile = Path.Combine(Paths.ConfigPath, "QuickWarp.cfg");

    private ModConfiguration()
    {
    }

    public static ModConfiguration Read()
    {
        var configuration = new ModConfiguration();
        if (!File.Exists(configuration._configFile))
        {
            Debug.LogError("[QuickWarp Config] Error reading config: Config File not found. Creating default config...");
            File.WriteAllText(
                configuration._configFile,
                "saveWarpKey = F6\n" +
                "loadWarpKey = F7");
            return configuration;
        }

        try
        {
            foreach (var line in File.ReadAllLines(configuration._configFile))
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }

                var parts = trimmed.Split(['='], 2);
                if (parts.Length != 2)
                {
                    continue;
                }

                var key = parts[0].Trim();
                var value = parts[1].Trim();

                if (key.Equals("saveWarpKey", StringComparison.OrdinalIgnoreCase))
                {
                    configuration.SaveWarpKey = value.ToKeyCode(KeyCode.F6);
                }
                else if (key.Equals("loadWarpKey", StringComparison.OrdinalIgnoreCase))
                {
                    configuration.LoadWarpKey = value.ToKeyCode(KeyCode.F7);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[QuickWarp Config] Error reading config: {ex.Message}");
        }

        return configuration;
    }
}

internal static class KeyCodeExtensions
{
    public static KeyCode ToKeyCode(this string str, KeyCode fallback = KeyCode.None)
    {
        if (string.IsNullOrWhiteSpace(str))
        {
            return fallback;
        }

        if (Enum.TryParse(str.Trim(), true, out KeyCode code))
        {
            return code;
        }

        return fallback;
    }
}

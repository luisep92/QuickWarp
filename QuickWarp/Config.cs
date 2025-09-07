using BepInEx;
using System;
using System.IO;
using UnityEngine;

// KeyCode docs https://docs.unity3d.com/6000.2/Documentation/ScriptReference/KeyCode.html
internal class Configuration
{
    public KeyCode saveWarpKey = KeyCode.F6;
    public KeyCode loadWarpKey = KeyCode.F7;
    private readonly string configFile = Path.Combine(Paths.ConfigPath, "QuickWarp.cfg");


    private Configuration()
    {

    }

    public static Configuration Read()
    {
        var ret = new Configuration();
        if (!File.Exists(ret.configFile))
        {
            Debug.LogError($"[QuickWarp Config] Error reading config: Config File not found. Creating default config...");
            File.WriteAllText(ret.configFile,
            "saveWarpKey = F6\n" +
            "loadWarpKey = F7");
            return ret;
        }

        try
        {
            foreach (var line in File.ReadAllLines(ret.configFile))
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#"))
                    continue;

                var parts = trimmed.Split(['='], 2);
                if (parts.Length != 2)
                    continue;

                var key = parts[0].Trim();
                var value = parts[1].Trim();

                if (key.Equals("saveWarpKey", StringComparison.OrdinalIgnoreCase))
                    ret.saveWarpKey = value.ToKeyCode(KeyCode.F6);

                if (key.Equals("loadWarpKey", StringComparison.OrdinalIgnoreCase))
                    ret.loadWarpKey = value.ToKeyCode(KeyCode.F7);
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[QuickWarp Config] Error reading config: {ex.Message}");
        }

        return ret;
    }
}

internal static class Extensions
{
    public static KeyCode ToKeyCode(this string str, KeyCode fallback = KeyCode.None)
    {
        if (string.IsNullOrWhiteSpace(str))
            return fallback;

        if (Enum.TryParse(str.Trim(), ignoreCase: true, out KeyCode code))
            return code;

        return fallback;
    }
}

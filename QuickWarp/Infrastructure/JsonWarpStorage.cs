using System;
using System.IO;
using System.Text;
using BepInEx;
using Newtonsoft.Json;
using QuickWarp.Domain;
using UnityEngine;

namespace QuickWarp.Infrastructure;

public sealed class JsonWarpStorage : IWarpStorage
{
    private readonly string _path = Path.Combine(Paths.ConfigPath, "QuickWarp_data.json");

    public bool TryLoad(out WarpData data)
    {
        data = new WarpData();
        if (!File.Exists(_path))
        {
            return false;
        }

        try
        {
            var raw = File.ReadAllText(_path, Encoding.UTF8);
            data = JsonConvert.DeserializeObject<WarpData>(raw) ?? new WarpData();
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[Warp] Failed to load warp data: {ex.Message}");
            return false;
        }
    }

    public bool TrySave(WarpData data, out string error)
    {
        try
        {
            var json = JsonConvert.SerializeObject(data, Formatting.Indented);
            File.WriteAllText(_path, json, Encoding.UTF8);
            error = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }
}

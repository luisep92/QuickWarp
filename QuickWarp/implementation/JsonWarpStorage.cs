using System.IO;
using System.Text;
using Newtonsoft.Json;
using BepInEx;

public sealed class JsonWarpStorage : IWarpStorage
{
    private readonly string _path = Path.Combine(Paths.ConfigPath, "QuickWarp_data.json");

    public bool TryLoad(out WarpData data)
    {
        data = null;
        if (!File.Exists(_path)) return false;
        try { data = JsonConvert.DeserializeObject<WarpData>(File.ReadAllText(_path, Encoding.UTF8)); return data != null; }
        catch { return false; }
    }

    public bool TrySave(WarpData data, out string error)
    {
        error = null;
        try
        {
            File.WriteAllText(_path, JsonConvert.SerializeObject(data, Formatting.Indented), Encoding.UTF8);
            return true;
        }
        catch (System.Exception ex) { error = ex.Message; return false; }
    }
}

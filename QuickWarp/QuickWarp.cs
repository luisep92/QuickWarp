using BepInEx;
using UnityEngine;

[BepInPlugin("com.luisep92.silksong.quickwarp", "Quick Warp", "2.0.0")]
public sealed class QuickWarp : BaseUnityPlugin
{
    private KeyCode _saveKey = KeyCode.F6;
    private KeyCode _loadKey = KeyCode.F7;
    private float _debounce = 0.2f;
    private float _lastKeyTime = -999f;

    private WarpController _warp;

    private void Awake()
    {
        var cfg = Configuration.Read();
        _saveKey = cfg.saveWarpKey;
        _loadKey = cfg.loadWarpKey;

        var scenes = new SceneSwitcher();
        var storage = new JsonWarpStorage();
        _warp = new WarpController(scenes, storage);

        Logger.LogInfo($"QuickWarp loaded. {_saveKey}=Save, {_loadKey}=Load");
    }

    private void Update()
    {
        if (Input.GetKeyDown(_saveKey))
            _warp.SaveHere();

        if (!Input.GetKeyDown(_loadKey)) return;
        if (Time.unscaledTime - _lastKeyTime < _debounce) return;
        _lastKeyTime = Time.unscaledTime;

        StartCoroutine(_warp.LoadSaved(ok =>
        {
            if (!ok) Logger.LogInfo("[Warp] Load failed or no warp saved.");
        }));
    }
}
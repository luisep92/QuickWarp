using BepInEx;
using QuickWarp.Application;
using QuickWarp.Configuration;
using QuickWarp.Infrastructure;
using UnityEngine;

namespace QuickWarp;

[BepInPlugin("com.luisep92.silksong.quickwarp", "Quick Warp", "2.0.0")]
public sealed class QuickWarp : BaseUnityPlugin
{
    private KeyCode _saveKey = KeyCode.F6;
    private KeyCode _loadKey = KeyCode.F7;
    private readonly float _debounce = 0.2f;
    private float _lastKeyTime = -999f;

    private WarpController _warp = null!;

    private void Awake()
    {
        var cfg = ModConfiguration.Read();
        _saveKey = cfg.SaveWarpKey;
        _loadKey = cfg.LoadWarpKey;

        var scenes = new SceneSwitcher();
        var storage = new JsonWarpStorage();
        _warp = new WarpController(scenes, storage);

        Logger.LogInfo($"QuickWarp loaded. {_saveKey}=Save, {_loadKey}=Load");
    }

    private void Update()
    {
        if (Input.GetKeyDown(_saveKey))
        {
            _warp.SaveHere();
        }

        if (!Input.GetKeyDown(_loadKey))
        {
            return;
        }

        if (Time.unscaledTime - _lastKeyTime < _debounce)
        {
            return;
        }

        _lastKeyTime = Time.unscaledTime;

        StartCoroutine(_warp.LoadSaved(ok =>
        {
            if (!ok)
            {
                Logger.LogInfo("[Warp] Load failed or no warp saved.");
            }
        }));
    }
}

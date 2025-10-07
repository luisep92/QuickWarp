using System;
using System.Collections;
using QuickWarp.Domain;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace QuickWarp.Application;

public sealed class WarpController
{
    private readonly ISceneSwitcher _scenes;
    private readonly IWarpStorage _storage;

    public WarpData Current { get; private set; }

    public WarpController(ISceneSwitcher scenes, IWarpStorage storage)
    {
        _scenes = scenes;
        _storage = storage;

        Current = _storage.TryLoad(out var loaded) ? loaded : new WarpData();
    }

    public void SaveHere()
    {
        var heroController = HeroController.instance;
        if (heroController == null)
        {
            Debug.LogError("[Warp] HeroController not available; cannot save warp.");
            return;
        }

        Current.Scene = SceneManager.GetActiveScene().name;
        Current.Position = heroController.transform.position;
        Current.Velocity = Vector2.zero;

        if (!_storage.TrySave(Current, out var error))
        {
            Debug.LogError($"[Warp] Save failed: {error}");
        }
        else
        {
            Debug.Log($"[Warp] Saved {Current.Scene} @ {Current.Position}");
        }
    }

    public IEnumerator LoadSaved(Action<bool> done)
    {
        if (string.IsNullOrEmpty(Current.Scene))
        {
            done(false);
            yield break;
        }

        var gameManager = GameManager.instance;
        if (gameManager == null)
        {
            done(false);
            yield break;
        }

        if (!string.Equals(gameManager.GetSceneNameString(), Current.Scene, StringComparison.Ordinal))
        {
            var switched = false;
            yield return _scenes.SwitchTo(Current.Scene, result => switched = result);
            if (!switched)
            {
                done(false);
                yield break;
            }
        }

        while (gameManager.IsInSceneTransition)
        {
            yield return null;
        }

        yield return new WaitForSeconds(0.1f);

        var heroController = HeroController.instance;
        if (heroController == null)
        {
            done(false);
            yield break;
        }

        heroController.ResetState();
        heroController.ResetVelocity();
        heroController.transform.SetPosition2D(Current.Position);

        done(true);
    }
}

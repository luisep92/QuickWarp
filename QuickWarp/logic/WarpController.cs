using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class WarpController
{
    private readonly ISceneSwitcher _scenes;
    private readonly IWarpStorage _storage;

    public WarpData Current { get; private set; }

    public WarpController(ISceneSwitcher scenes, IWarpStorage storage)
    {
        _scenes = scenes;
        _storage = storage;
        if (_storage.TryLoad(out var loaded))
            Current = loaded;
        if (Current == null)
            Current = new WarpData();
    }

    public void SaveHere()
    {
        var heroController = HeroController.instance;
        
        Current.Scene = SceneManager.GetActiveScene().name;
        Current.Position = heroController.transform.position;
        Current.Velocity = Vector2.zero;

        if (!_storage.TrySave(Current, out var err))
            Debug.LogError($"[Warp] Save failed: {err}");
        else
            Debug.Log($"[Warp] Saved {Current.Scene} @ {Current.Position}");
    }

    public IEnumerator LoadSaved(System.Action<bool> done)
    {
        if (string.IsNullOrEmpty(Current?.Scene)) { done(false); yield break; }

        var target = Current.Scene;
        var gameManager = GameManager.instance;
        
        if (gameManager.GetSceneNameString() != target)
        {
            bool switched = false;
            yield return _scenes.SwitchTo(target, r => switched = r);
            if (!switched) { done(false); yield break; }
        }

        while (gameManager.IsInSceneTransition)
            yield return null;
            
        yield return new WaitForSeconds(0.1f);
        
        var heroController = HeroController.instance;
        heroController.ResetState();
        heroController.ResetVelocity();
        heroController.transform.SetPosition2D(Current.Position);
        
        done(true);
    }
}
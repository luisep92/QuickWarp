using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class WarpController
{
    private readonly IPlayerLocator _players;
    private readonly ISceneSwitcher _scenes;
    private readonly IWarpStorage _storage;

    private readonly float _playerFindTimeout = 8f;

    public WarpData Current { get; private set; }

    public WarpController(IPlayerLocator players, ISceneSwitcher scenes, IWarpStorage storage)
    {
        _players = players;
        _scenes = scenes;
        _storage = storage;
        if (_storage.TryLoad(out var loaded))
            Current = loaded;
        if (Current == null)
            Current = new WarpData();
    }

    public void SaveHere()
    {
        var player = _players.FindPlayer();
        if (!player) { Debug.LogWarning("[Warp] Player not found."); return; }

        var rb = player.GetComponent<Rigidbody2D>();
        Current.Scene = SceneManager.GetActiveScene().name;
        Current.Position = player.transform.position;
        Current.Velocity = rb ? rb.linearVelocity : Vector2.zero;

        if (!_storage.TrySave(Current, out var err))
            Debug.LogError($"[Warp] Save failed: {err}");
        else
            Debug.Log($"[Warp] Saved {Current.Scene} @ {Current.Position}");
    }

    public IEnumerator LoadSaved(System.Action<bool> done)
    {
        if (string.IsNullOrEmpty(Current?.Scene)) { done(false); yield break; }

        var target = Current.Scene;
        if (SceneManager.GetActiveScene().name != target)
        {
            bool switched = false;
            yield return _scenes.SwitchTo(target, r => switched = r);
            if (!switched) { done(false); yield break; }
        }

        // After load: wait for an active player and restore transform
        GameObject player = null;
        float start = Time.unscaledTime;
        while ((Time.unscaledTime - start) < _playerFindTimeout)
        {
            player = _players.FindPlayer();
            if (player && player.activeInHierarchy) break;
            yield return null;
        }
        if (!player || !player.activeInHierarchy) { Debug.LogWarning("[Warp] Player not active."); done(false); yield break; }

        var rb = player.GetComponent<Rigidbody2D>();
        yield return null; // let physics/camera settle one frame

        player.transform.position = Current.Position;
        if (rb) { rb.linearVelocity = Current.Velocity; rb.angularVelocity = 0f; }
        Physics2D.SyncTransforms();
        yield return new WaitForEndOfFrame();

        Debug.Log($"[Warp] Teleported to {Current.Scene} @ {Current.Position}");
        done(true);
    }
}

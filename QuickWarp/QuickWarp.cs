using BepInEx;
using Newtonsoft.Json;
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

// ============================================================================
//  Silksong Quick Warp
//  Keybinds: F6 (save warp) / F7 (load warp)
//  Notes:
//   - Does NOT persist game state, only scene + transform (+ velocity).
//   - Tries to use the game's internal scene loader (via reflection) first.
//   - Falls back to Unity SceneManager strategies if needed.
// ============================================================================

[BepInPlugin("com.tu.nick.silksong.quickwarp", "Silksong Quick Warp", "1.3.0")]
public class QuickWarp : BaseUnityPlugin
{
    // ----------------------------- Types ------------------------------------
    [Serializable]
    public class WarpData
    {
        public string scene;
        public Vector3 pos;
        public Vector2 vel;
    }

    // ---------------------------- Constants ---------------------------------
    private const KeyCode SaveKey = KeyCode.F6;
    private const KeyCode LoadKey = KeyCode.F7;

    private const float KeyDebounce = 0.20f; // seconds
    private const float LoaderWaitTimeout = 8f;
    private const float SceneAsyncTimeout = 6f;
    private const float AdditiveAsyncTimeout = 8f;
    private const float PlayerFindTimeout = 8f;

    // ----------------------------- Paths ------------------------------------
    private static string SavePath => Path.Combine(Paths.ConfigPath, "silksong_quickwarp.json");

    // ----------------------------- State ------------------------------------
    private WarpData _warp;
    private bool _isWarpLoading;
    private string _targetScene;
    private float _lastWarpKeyTime = -999f;
    private bool _restoreStarted; // avoid double RestoreAfterLoad()

    // Cached internal loader (reflection)
    private MethodInfo _cachedLoaderMethod;
    private object _cachedLoaderInstance;

    // ---------------------------- Unity Hooks -------------------------------
    private void Awake()
    {
        LoadSavedWarpIfAny();

        SceneManager.sceneLoaded += OnSceneLoaded;
        SceneManager.activeSceneChanged += OnActiveSceneChanged;

        Logger.LogInfo("QuickWarp loaded. F6 = Save warp, F7 = Load warp");

        TryFindInternalSceneLoader();
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.activeSceneChanged -= OnActiveSceneChanged;
    }

    private void Update()
    {
        if (Input.GetKeyDown(SaveKey))
            SaveWarpHere();

        if (!Input.GetKeyDown(LoadKey)) return;

        var since = Time.unscaledTime - _lastWarpKeyTime;
        if (_isWarpLoading)
        {
            Logger.LogInfo("[Warp-Key] F7 ignored: warp already running.");
            return;
        }
        if (since < KeyDebounce)
        {
            Logger.LogInfo($"[Warp-Key] F7 debounced ({since:0.000}s).");
            return;
        }
        if (_warp == null || string.IsNullOrEmpty(_warp.scene))
        {
            Logger.LogInfo("[Warp-Key] No warp saved.");
            return;
        }

        string active = SceneManager.GetActiveScene().name;
        Logger.LogInfo($"[Warp-Key] F7 -> Active='{active}', Target='{_warp.scene}', Pos={_warp.pos}");
        StartCoroutine(LoadWarp());
    }

    // --------------------------- Save / Load --------------------------------
    private void SaveWarpHere()
    {
        var player = FindPlayer();
        if (!player)
        {
            Logger.LogWarning("[Save] Player not found.");
            return;
        }

        var scene = SceneManager.GetActiveScene().name;
        var rb = player.GetComponent<Rigidbody2D>();

        _warp ??= new WarpData();
        _warp.scene = scene;
        _warp.pos = player.transform.position;
        _warp.vel = rb ? rb.linearVelocity : Vector2.zero;

        try
        {
            var json = JsonConvert.SerializeObject(_warp, Formatting.Indented);
            File.WriteAllText(SavePath, json, Encoding.UTF8);
            Logger.LogInfo($"[Save] Warp saved: {_warp.scene} @ {_warp.pos}");
        }
        catch (Exception e)
        {
            Logger.LogError($"[Save] Could not write warp file: {e.Message}");
        }
    }

    private IEnumerator LoadWarp()
    {
        if (_isWarpLoading || Time.unscaledTime - _lastWarpKeyTime < KeyDebounce)
            yield break;
        _lastWarpKeyTime = Time.unscaledTime;

        if (_warp == null || string.IsNullOrEmpty(_warp.scene))
        {
            Logger.LogInfo("[Warp] No warp saved.");
            yield break;
        }

        var activeBefore = SceneManager.GetActiveScene().name;
        Logger.LogInfo($"[Warp] Begin. Active='{activeBefore}', Target='{_warp.scene}'");

        // Same scene → just restore
        if (activeBefore == _warp.scene)
        {
            Logger.LogInfo("[Warp] Already in target scene. Restoring position directly...");
            _restoreStarted = true;
            yield return StartCoroutine(RestoreAfterLoad());
            yield break;
        }

        _isWarpLoading = true;
        _targetScene = _warp.scene;

        // 0) Try the game's internal loader first
        if (TryUseInternalLoader(_targetScene))
        {
            float t = 0f;
            while (SceneManager.GetActiveScene().name != _targetScene && t < LoaderWaitTimeout)
            {
                t += Time.unscaledDeltaTime;
                yield return null;
            }

            if (SceneManager.GetActiveScene().name == _targetScene)
            {
                Logger.LogInfo("[Warp] Internal loader switched scene successfully.");
                _restoreStarted = true;
                yield return StartCoroutine(RestoreAfterLoad());
                yield break;
            }

            Logger.LogWarning("[Warp] Internal loader did not switch ActiveScene within timeout.");
        }
        else
        {
            Logger.LogInfo("[Warp] No internal loader found (or invocation failed). Falling back to SceneManager.");
        }

        // 1) Unity async (Single)
        bool switched = false;
        AsyncOperation async = null;
        try
        {
            Logger.LogInfo("[Warp] Trying LoadSceneAsync (Single)...");
            async = SceneManager.LoadSceneAsync(_targetScene, LoadSceneMode.Single);
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"[Warp] LoadSceneAsync threw: {ex.Message}");
        }

        if (async != null)
        {
            async.allowSceneActivation = true;
            float t = 0f;
            while (SceneManager.GetActiveScene().name != _targetScene && t < SceneAsyncTimeout)
            {
                t += Time.unscaledDeltaTime;
                yield return null;
            }
            switched = SceneManager.GetActiveScene().name == _targetScene;
            Logger.LogInfo($"[Warp] Async result: switched={switched}");
        }
        else
        {
            Logger.LogInfo("[Warp] LoadSceneAsync returned null.");
        }

        // 2) Unity sync (Single)
        if (!switched)
        {
            Logger.LogInfo("[Warp] Trying LoadScene (Single) sync...");
            switched = TryLoadSceneSync(_targetScene) && SceneManager.GetActiveScene().name == _targetScene;
            Logger.LogInfo($"[Warp] Sync result: switched={switched}");
        }

        // 3) Unity additive fallback
        if (!switched)
        {
            var oldActive = SceneManager.GetActiveScene();
            Logger.LogInfo("[Warp] Trying Additive fallback (load + SetActive + unload previous)...");

            bool addOk = false;
            AsyncOperation addOp = null;
            try { addOp = SceneManager.LoadSceneAsync(_targetScene, LoadSceneMode.Additive); }
            catch (Exception ex) { Logger.LogWarning($"[Warp] Additive LoadSceneAsync threw: {ex.Message}"); }

            if (addOp != null)
            {
                float t = 0f;
                while (!(SceneManager.GetSceneByName(_targetScene).IsValid() &&
                         SceneManager.GetSceneByName(_targetScene).isLoaded) && t < AdditiveAsyncTimeout)
                {
                    t += Time.unscaledDeltaTime;
                    yield return null;
                }

                var tgt = SceneManager.GetSceneByName(_targetScene);
                if (tgt.IsValid() && tgt.isLoaded)
                {
                    addOk = SceneManager.SetActiveScene(tgt);
                    Logger.LogInfo($"[Warp] SetActiveScene after additive: {addOk}");
                }
                else
                {
                    Logger.LogWarning("[Warp] Additive load timeout or invalid scene.");
                }
            }
            else
            {
                Logger.LogWarning("[Warp] Additive LoadSceneAsync returned null.");
            }

            if (addOk)
            {
                try
                {
                    Logger.LogInfo("[Warp] Unloading previous scene...");
                    SceneManager.UnloadSceneAsync(oldActive);
                }
                catch (Exception ex)
                {
                    Logger.LogWarning($"[Warp] UnloadSceneAsync threw: {ex.Message}");
                }

                yield return null; // let SceneManager settle
                yield return null;
                switched = SceneManager.GetActiveScene().name == _targetScene;
                Logger.LogInfo($"[Warp] Additive result: switched={switched}");
            }
        }

        if (switched)
        {
            _restoreStarted = true;
            yield return StartCoroutine(RestoreAfterLoad());
        }
        else
        {
            Logger.LogInfo("[Warp] Could not change scene. Likely not in BuildSettings and requires the game's internal loader.");
            _isWarpLoading = false;
            _targetScene = null;
        }
    }

    // -------------------- Internal scene loader (reflection) -----------------
    private void TryFindInternalSceneLoader()
    {
        if (_cachedLoaderMethod != null) return;

        try
        {
            var asm = AppDomain.CurrentDomain.GetAssemblies()
                      .FirstOrDefault(a => a.GetName().Name == "Assembly-CSharp");
            if (asm == null) { Logger.LogInfo("[Loader] Assembly-CSharp not found."); return; }

            string[] preferred = { "BeginSceneTransition", "ChangeScene", "GoToScene" };
            string[] loadNames = { "LoadScene", "LoadLevel", "StartScene" };

            MethodInfo best = null;
            object bestInstance = null;

            foreach (var type in asm.GetTypes())
            {
                if (type.Name.StartsWith("<")) continue;

                object instance = TryGetSingletonInstance(type);

                foreach (var m in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
                {
                    string n = m.Name;
                    if (n.IndexOf("Unload", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    if (n.IndexOf("Preload", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    if (n.IndexOf("Download", StringComparison.OrdinalIgnoreCase) >= 0) continue;

                    var pars = m.GetParameters();
                    if (pars.Length == 0 || pars[0].ParameterType != typeof(string))
                        continue;

                    bool isPreferred = preferred.Any(p => n.IndexOf(p, StringComparison.OrdinalIgnoreCase) >= 0);
                    bool isLoadish = loadNames.Any(p => n.Equals(p, StringComparison.OrdinalIgnoreCase) || n.IndexOf(p, StringComparison.OrdinalIgnoreCase) >= 0);
                    if (!isPreferred && !isLoadish) continue;

                    int score = isPreferred ? 2 : 1;
                    int bestScore = (best == null) ? -1 :
                        (preferred.Any(p => best.Name.IndexOf(p, StringComparison.OrdinalIgnoreCase) >= 0) ? 2 : 1);

                    if (best == null || score > bestScore)
                    {
                        best = m;
                        bestInstance = m.IsStatic ? null : instance;
                    }
                }
            }

            if (best != null)
            {
                if (!best.IsStatic && bestInstance == null)
                {
                    var found = Resources.FindObjectsOfTypeAll(best.DeclaringType);
                    if (found is { Length: > 0 }) bestInstance = found[0];
                }

                _cachedLoaderMethod = best;
                _cachedLoaderInstance = best.IsStatic ? null : bestInstance;

                string parms = string.Join(", ", best.GetParameters().Select(p => p.ParameterType.Name));
                Logger.LogInfo($"[Loader] Using {best.DeclaringType.FullName}.{best.Name}({parms})" +
                               (best.IsStatic ? " [static]" : (_cachedLoaderInstance != null ? " [instance found]" : " [instance missing]")));
            }
            else
            {
                Logger.LogInfo("[Loader] No suitable loader method found in Assembly-CSharp.");
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"[Loader] Discovery failed: {ex.Message}");
        }
    }

    private object TryGetSingletonInstance(Type type)
    {
        try
        {
            var props = type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            var p = props.FirstOrDefault(pp =>
                pp.PropertyType == type &&
                (pp.Name.Equals("Instance", StringComparison.OrdinalIgnoreCase) ||
                 pp.Name.IndexOf("Singleton", StringComparison.OrdinalIgnoreCase) >= 0));
            if (p != null) return p.GetValue(null);

            var fields = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            var f = fields.FirstOrDefault(ff =>
                ff.FieldType == type &&
                (ff.Name.Equals("Instance", StringComparison.OrdinalIgnoreCase) ||
                 ff.Name.Equals("_instance", StringComparison.OrdinalIgnoreCase) ||
                 ff.Name.Equals("s_instance", StringComparison.OrdinalIgnoreCase) ||
                 ff.Name.IndexOf("singleton", StringComparison.OrdinalIgnoreCase) >= 0));
            if (f != null) return f.GetValue(null);
        }
        catch { /* ignored */ }

        return null;
    }

    private bool TryUseInternalLoader(string sceneName)
    {
        try
        {
            if (_cachedLoaderMethod == null) TryFindInternalSceneLoader();
            if (_cachedLoaderMethod == null) return false;

            if (!_cachedLoaderMethod.IsStatic && _cachedLoaderInstance == null)
            {
                var all = Resources.FindObjectsOfTypeAll(_cachedLoaderMethod.DeclaringType);
                if (all is { Length: > 0 }) _cachedLoaderInstance = all[0];
            }

            if (!_cachedLoaderMethod.IsStatic && _cachedLoaderInstance == null)
            {
                Logger.LogWarning("[Loader] Instance method requires a target, but no instance was found.");
                return false;
            }

            var pars = _cachedLoaderMethod.GetParameters();
            object[] args;
            if (pars.Length == 1)
            {
                args = new object[] { sceneName };
            }
            else
            {
                args = new object[pars.Length];
                args[0] = sceneName;
                for (int i = 1; i < pars.Length; i++)
                {
                    var pt = pars[i].ParameterType;
                    if (pt == typeof(bool)) args[i] = false;
                    else if (pt == typeof(int)) args[i] = 0;
                    else if (pt == typeof(float)) args[i] = 0f;
                    else if (pt == typeof(string)) args[i] = string.Empty;
                    else if (pt.IsEnum || pt.IsValueType) args[i] = Activator.CreateInstance(pt);
                    else args[i] = null;
                }
            }

            Logger.LogInfo($"[Loader] Invoking {_cachedLoaderMethod.DeclaringType.Name}.{_cachedLoaderMethod.Name} for '{sceneName}'...");
            _cachedLoaderMethod.Invoke(_cachedLoaderInstance, args);
            return true;
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"[Loader] Invocation failed: {ex.Message}");
            return false;
        }
    }

    // --------------------------- Scene Events -------------------------------
    private void OnSceneLoaded(Scene s, LoadSceneMode _)
    {
        MaybeStartRestore(s.name);
    }

    private void OnActiveSceneChanged(Scene _, Scene newScene)
    {
        MaybeStartRestore(newScene.name);
    }

    private void MaybeStartRestore(string sceneName)
    {
        if (_isWarpLoading && !string.IsNullOrEmpty(_targetScene) && sceneName == _targetScene)
        {
            Logger.LogInfo($"[Warp] Entered '{sceneName}'. Restoring position...");
            if (_restoreStarted) return;
            _restoreStarted = true;
            StartCoroutine(RestoreAfterLoad());
        }
    }

    // After the scene is switched, wait for a real active player and restore transform.
    private IEnumerator RestoreAfterLoad()
    {
        if (!_isWarpLoading) _isWarpLoading = true;

        GameObject player = null;
        float start = Time.unscaledTime;
        while ((Time.unscaledTime - start) < PlayerFindTimeout)
        {
            player = FindPlayer(); // may return inactive too
            if (player != null && player.activeInHierarchy) break;
            yield return null;
        }

        if (player == null || !player.activeInHierarchy)
        {
            Logger.LogWarning("[Warp] Player not found/active after scene load timeout. Aborting restore.");
            _isWarpLoading = false;
            _targetScene = null;
            _restoreStarted = false;
            yield break;
        }

        var rb = player.GetComponent<Rigidbody2D>();

        // Let physics/camera settle for one frame
        yield return null;

        player.transform.position = _warp.pos;
        if (rb)
        {
            rb.linearVelocity = _warp.vel;
            rb.angularVelocity = 0f;
        }
        Physics2D.SyncTransforms();

        // No manual camera snapping
        yield return new WaitForEndOfFrame();

        Logger.LogInfo($"Teleport to {_warp.scene} @ {_warp.pos}");
        _isWarpLoading = false;
        _targetScene = null;
        _restoreStarted = false;
    }

    // --------------------------- Utilities ----------------------------------
    private void LoadSavedWarpIfAny()
    {
        if (!File.Exists(SavePath)) { _warp = new WarpData(); return; }
        try
        {
            _warp = JsonConvert.DeserializeObject<WarpData>(File.ReadAllText(SavePath, Encoding.UTF8));
        }
        catch (Exception e)
        {
            Logger.LogError($"[Load] {e.Message}");
            _warp = new WarpData();
        }
    }

    // Prefer active instances; fallback to inactive search via Resources
    private GameObject FindPlayer()
    {
        var byTag = GameObject.FindWithTag("Player");
        if (byTag != null) return byTag;

        foreach (var t in GameObject.FindObjectsOfType<Transform>())
        {
            if (t == null) continue;
            string n = t.name;
            if (string.IsNullOrEmpty(n)) continue;
            if (n.Equals("Player", StringComparison.OrdinalIgnoreCase) ||
                n.IndexOf("Hornet", StringComparison.OrdinalIgnoreCase) >= 0)
                return t.gameObject;
        }

        foreach (var t in Resources.FindObjectsOfTypeAll<Transform>())
        {
            if (t == null) continue;
            var go = t.gameObject;
            string n = go.name;
            if (string.IsNullOrEmpty(n)) continue;

            if (n.Equals("Player", StringComparison.OrdinalIgnoreCase) ||
                n.IndexOf("Hornet", StringComparison.OrdinalIgnoreCase) >= 0 ||
                go.CompareTag("Player"))
                return go; // may be inactive; caller will only use if activeInHierarchy
        }

        return null;
    }

    private bool TryLoadSceneSync(string sceneName)
    {
        try { SceneManager.LoadScene(sceneName, LoadSceneMode.Single); return true; }
        catch (Exception ex) { Logger.LogWarning($"[Warp] LoadScene sync threw: {ex.Message}"); return false; }
    }
}

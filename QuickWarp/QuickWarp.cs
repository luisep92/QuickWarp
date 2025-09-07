using BepInEx;
using UnityEngine;
using UnityEngine.SceneManagement;
using System.IO;
using System.Collections;
using System.Text;
using System;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;

[BepInPlugin("com.tu.nick.silksong.quickwarp", "Silksong Quick Warp", "1.3.0")]
public class QuickWarp : BaseUnityPlugin
{
    [Serializable]
    public class WarpData
    {
        public string scene;
        public Vector3 pos;
        public Vector2 vel;
    }

    private static string SavePath => Path.Combine(Paths.ConfigPath, "silksong_quickwarp.json");
    private WarpData warp;

    // Warp state
    private bool isWarpLoading = false;
    private string targetScene = null;
    private float lastWarpTime = -999f;
    private bool restoreStarted = false; // avoid multiple restores

    // Cached internal loader (reflection)
    private MethodInfo cachedLoaderMethod;
    private object cachedLoaderInstance;

    void Awake()
    {
        // Load saved warp file (if any)
        if (File.Exists(SavePath))
        {
            try { warp = JsonConvert.DeserializeObject<WarpData>(File.ReadAllText(SavePath, Encoding.UTF8)); }
            catch (Exception e) { Logger.LogError(e); }
        }
        if (warp == null) warp = new WarpData();

        SceneManager.sceneLoaded += OnSceneLoaded;
        SceneManager.activeSceneChanged += OnActiveSceneChanged;

        Logger.LogInfo("QuickWarp loaded. F6=Save warp, F7=Load warp");
        TryFindInternalSceneLoader();
    }

    void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.activeSceneChanged -= OnActiveSceneChanged;
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.F6))
            SaveWarpHere();

        if (Input.GetKeyDown(KeyCode.F7))
        {
            float since = Time.unscaledTime - lastWarpTime;
            if (isWarpLoading) { Logger.LogInfo("[Warp-Key] F7 ignored: warp already running."); return; }
            if (since < 0.20f) { Logger.LogInfo($"[Warp-Key] F7 debounced ({since:0.000}s)."); return; }
            if (warp == null || string.IsNullOrEmpty(warp.scene)) { Logger.LogInfo("[Warp-Key] No warp saved."); return; }

            string active = SceneManager.GetActiveScene().name;
            Logger.LogInfo($"[Warp-Key] F7 -> Active='{active}', Target='{warp.scene}', Pos={warp.pos}");
            StartCoroutine(LoadWarp());
        }
    }

    void SaveWarpHere()
    {
        var scene = SceneManager.GetActiveScene().name;
        var player = FindPlayer();
        if (!player) { Logger.LogWarning("Player not found."); return; }

        var rb = player.GetComponent<Rigidbody2D>();
        if (warp == null) warp = new WarpData();
        warp.scene = scene;
        warp.pos = player.transform.position;
        warp.vel = rb ? rb.linearVelocity : Vector2.zero;

        File.WriteAllText(SavePath, JsonConvert.SerializeObject(warp, Formatting.Indented), Encoding.UTF8);
        Logger.LogInfo($"Warp saved: {warp.scene} @ {warp.pos}");
    }

    IEnumerator LoadWarp()
    {
        if (isWarpLoading || Time.unscaledTime - lastWarpTime < 0.20f)
            yield break;
        lastWarpTime = Time.unscaledTime;

        if (warp == null || string.IsNullOrEmpty(warp.scene)) { Logger.LogInfo("[Warp] No warp saved."); yield break; }

        string activeBefore = SceneManager.GetActiveScene().name;
        Logger.LogInfo($"[Warp] Begin. Active='{activeBefore}', Target='{warp.scene}'");

        // Same-scene: just restore
        if (activeBefore == warp.scene)
        {
            Logger.LogInfo("[Warp] Already in target scene. Restoring position directly...");
            restoreStarted = true;
            yield return StartCoroutine(RestoreAfterLoad());
            yield break;
        }

        isWarpLoading = true;
        targetScene = warp.scene;

        // 0) Try internal loader first
        if (TryUseInternalLoader(targetScene))
        {
            const float MAX = 8f;
            float t = 0f;
            while (SceneManager.GetActiveScene().name != targetScene && t < MAX)
            {
                t += Time.unscaledDeltaTime;
                yield return null;
            }

            if (SceneManager.GetActiveScene().name == targetScene)
            {
                Logger.LogInfo("[Warp] Internal loader switched scene successfully.");
                restoreStarted = true;
            yield return StartCoroutine(RestoreAfterLoad());
                yield break;
            }
            else
            {
                Logger.LogWarning("[Warp] Internal loader did not switch ActiveScene within timeout.");
            }
        }
        else
        {
            Logger.LogInfo("[Warp] No internal loader found (or invocation failed). Falling back to Unity SceneManager.");
        }

        // 1) Unity async (Single)
        bool switched = false;
        AsyncOperation op = null;
        try
        {
            Logger.LogInfo("[Warp] Trying LoadSceneAsync (Single)...");
            op = SceneManager.LoadSceneAsync(targetScene, LoadSceneMode.Single);
        }
        catch (Exception ex) { Logger.LogWarning($"[Warp] LoadSceneAsync threw: {ex.Message}"); }

        if (op != null)
        {
            op.allowSceneActivation = true;
            const float TIMEOUT = 6f;
            float t = 0f;
            while (SceneManager.GetActiveScene().name != targetScene && t < TIMEOUT)
            {
                t += Time.unscaledDeltaTime;
                yield return null;
            }
            switched = (SceneManager.GetActiveScene().name == targetScene);
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
            if (TryLoadSceneSync(targetScene))
            {
                yield return null; yield return null;
                switched = (SceneManager.GetActiveScene().name == targetScene);
                Logger.LogInfo($"[Warp] Sync result: switched={switched}");
            }
            else
            {
                Logger.LogInfo("[Warp] Sync LoadScene threw/failed.");
            }
        }

        // 3) Unity additive fallback
        if (!switched)
        {
            var oldActive = SceneManager.GetActiveScene();
            Logger.LogInfo("[Warp] Trying Additive fallback (load + SetActive + unload previous)...");
            bool addOk = false;
            AsyncOperation addOp = null;
            try { addOp = SceneManager.LoadSceneAsync(targetScene, LoadSceneMode.Additive); }
            catch (Exception ex) { Logger.LogWarning($"[Warp] Additive LoadSceneAsync threw: {ex.Message}"); }

            if (addOp != null)
            {
                const float ATIMEOUT = 8f;
                float t = 0f;
                while (!(SceneManager.GetSceneByName(targetScene).IsValid() &&
                         SceneManager.GetSceneByName(targetScene).isLoaded) && t < ATIMEOUT)
                {
                    t += Time.unscaledDeltaTime;
                    yield return null;
                }

                var tgt = SceneManager.GetSceneByName(targetScene);
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

                yield return null; yield return null;
                switched = (SceneManager.GetActiveScene().name == targetScene);
                Logger.LogInfo($"[Warp] Additive result: switched={switched}");
            }
        }

        if (switched)
        {
            restoreStarted = true;
            yield return StartCoroutine(RestoreAfterLoad());
        }
        else
        {
            Logger.LogInfo("[Warp] Could not change scene. Likely not listed in BuildSettings and requires the game's internal loader.");
            isWarpLoading = false;
            targetScene = null;
        }
    }

    // ---------- Internal scene loader discovery/invocation ----------

    void TryFindInternalSceneLoader()
    {
        if (cachedLoaderMethod != null) return;

        try
        {
            var asm = AppDomain.CurrentDomain.GetAssemblies()
                       .FirstOrDefault(a => a.GetName().Name == "Assembly-CSharp");
            if (asm == null) { Logger.LogInfo("[Loader] Assembly-CSharp not found."); return; }

            // Prefer these names first
            string[] preferred = { "BeginSceneTransition", "ChangeScene", "GoToScene" };
            // Accept load-ish names but exclude Unload/Preload/Download
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
                    bool isLoadish   = loadNames.Any(p => n.Equals(p, StringComparison.OrdinalIgnoreCase) || n.IndexOf(p, StringComparison.OrdinalIgnoreCase) >= 0);
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
                    if (found != null && found.Length > 0)
                        bestInstance = found[0];
                }

                cachedLoaderMethod = best;
                cachedLoaderInstance = best.IsStatic ? null : bestInstance;

                string parms = string.Join(", ", best.GetParameters().Select(p => p.ParameterType.Name));
                Logger.LogInfo($"[Loader] Using {best.DeclaringType.FullName}.{best.Name}({parms})"
                             + (best.IsStatic ? " [instance missing]" : (cachedLoaderInstance != null ? " [instance found]" : " [instance missing]")));
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

    object TryGetSingletonInstance(Type type)
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
        catch { }
        return null;
    }

    bool TryUseInternalLoader(string sceneName)
    {
        try
        {
            if (cachedLoaderMethod == null) TryFindInternalSceneLoader();
            if (cachedLoaderMethod == null) return false;

            if (!cachedLoaderMethod.IsStatic && cachedLoaderInstance == null)
            {
                var all = Resources.FindObjectsOfTypeAll(cachedLoaderMethod.DeclaringType);
                if (all != null && all.Length > 0)
                    cachedLoaderInstance = all[0];
            }

            if (!cachedLoaderMethod.IsStatic && cachedLoaderInstance == null)
            {
                Logger.LogWarning("[Loader] Instance method requires a target, but no instance was found.");
                return false;
            }

            var pars = cachedLoaderMethod.GetParameters();
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
                    else if (pt.IsEnum) args[i] = Activator.CreateInstance(pt);
                    else if (pt.IsValueType) args[i] = Activator.CreateInstance(pt);
                    else args[i] = null;
                }
            }

            Logger.LogInfo($"[Loader] Invoking {cachedLoaderMethod.DeclaringType.Name}.{cachedLoaderMethod.Name} for '{sceneName}'...");
            cachedLoaderMethod.Invoke(cachedLoaderInstance, args);
            return true;
        }
        catch (Exception ex)
        {
            Logger.LogWarning($"[Loader] Invocation failed: {ex.Message}");
            return false;
        }
    }

    // ---------- Unity fallbacks / restore ----------

    bool TryLoadSceneSync(string sceneName)
    {
        try { SceneManager.LoadScene(sceneName, LoadSceneMode.Single); return true; }
        catch (Exception ex) { Logger.LogWarning($"[Warp] LoadScene sync threw: {ex.Message}"); return false; }
    }

    void OnSceneLoaded(Scene s, LoadSceneMode m)
    {
        if (isWarpLoading && !string.IsNullOrEmpty(targetScene) && s.name == targetScene)
        {
            Logger.LogInfo($"[Warp] SceneLoaded: '{s.name}'. Restoring position...");
            if (restoreStarted) return;
            restoreStarted = true;
            StartCoroutine(RestoreAfterLoad());
        }
    }

    void OnActiveSceneChanged(Scene oldScene, Scene newScene)
    {
        if (isWarpLoading && !string.IsNullOrEmpty(targetScene) && newScene.name == targetScene)
        {
            Logger.LogInfo($"[Warp] ActiveSceneChanged: '{newScene.name}'. Restoring position...");
            if (restoreStarted) return;
            restoreStarted = true;
            StartCoroutine(RestoreAfterLoad());
        }
    }

    // After the scene is switched, wait for the real player to be active and then restore transform.
    // No manual camera movement here.
    IEnumerator RestoreAfterLoad()
    {
        if (!isWarpLoading) isWarpLoading = true;

        // Wait (unscaled) until there is an ACTIVE player in hierarchy
        GameObject player = null;
        float start = Time.unscaledTime;
        const float MAX_WAIT = 8f; // real seconds

        while ((Time.unscaledTime - start) < MAX_WAIT)
        {
            player = FindPlayer();                 // may return inactive too
            if (player != null && player.activeInHierarchy)
                break;

            // If we found it but it's inactive, do NOT force-activate yet:
            // many games finish spawning/anim/brain on the frame they enable it.
            yield return null;
        }

        if (player == null || !player.activeInHierarchy)
        {
            Logger.LogWarning("[Warp] Player not found/active after scene load timeout. Aborting restore.");
            isWarpLoading = false;
            targetScene = null;
            restoreStarted = false;
            yield break;
        }

        // From here we have an active player object
        var rb = player.GetComponent<Rigidbody2D>();

        // Wait a frame to let physics/camera settle
        yield return null;

        // Restore transform
        player.transform.position = warp.pos;

        if (rb)
        {
            rb.linearVelocity = warp.vel;
            rb.angularVelocity = 0f;
        }

        Physics2D.SyncTransforms();

        // No manual camera snapping here
        yield return new WaitForEndOfFrame();
        
        Logger.LogInfo($"Teleport to {warp.scene} @ {warp.pos}");
        isWarpLoading = false;
        targetScene = null;
        restoreStarted = false;
    }


    object[] BuildArgs(ParameterInfo[] pars, Transform player)
    {
        if (pars == null || pars.Length == 0) return Array.Empty<object>();
        var args = new object[pars.Length];
        for (int i = 0; i < pars.Length; i++)
        {
            var pt = pars[i].ParameterType;
            if (pt == typeof(Transform)) args[i] = player;
            else if (pt == typeof(Vector3)) args[i] = player.position;
            else if (pt == typeof(Vector2)) args[i] = (Vector2)player.position;
            else if (pt == typeof(float)) args[i] = 0f;
            else if (pt == typeof(int)) args[i] = 0;
            else if (pt == typeof(bool)) args[i] = true;
            else if (pt.IsEnum) args[i] = Activator.CreateInstance(pt);
            else if (pt.IsValueType) args[i] = Activator.CreateInstance(pt);
            else args[i] = null;
        }
        return args;
    }

    // Find the player; prefer active instances. Falls back to inactive search via Resources.
    GameObject FindPlayer()
    {
        // Active fast-paths
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

        // Fallback: inactive too
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

}

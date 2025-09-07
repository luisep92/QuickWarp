// implementation/SceneSwitcher.cs
using System;
using System.Collections;                 // IEnumerator
using System.Linq;
using System.Reflection;                   // MethodInfo, Type
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class SceneSwitcher : ISceneSwitcher
{
    private readonly float _loaderWait = 8f, _asyncWait = 6f, _additiveWait = 8f;
    private MethodInfo _loaderMethod;
    private object _loaderInstance;

    public IEnumerator SwitchTo(string sceneName, Action<bool> done)
    {
        var active = SceneManager.GetActiveScene().name;
        if (active == sceneName) { done(true); yield break; }

        // 1) Internal loader first (invoke, luego esperar)
        if (TryFindLoader() && TryInvokeLoader(sceneName))
        {
            float t = 0f;
            while (SceneManager.GetActiveScene().name != sceneName && t < _loaderWait)
            { t += Time.unscaledDeltaTime; yield return null; }
            if (SceneManager.GetActiveScene().name == sceneName) { done(true); yield break; }
        }

        // 2) Async Single  (NO yields dentro del try/catch)
        AsyncOperation op = null;
        try { op = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single); }
        catch (Exception ex) { Debug.LogWarning($"[Warp] Async load threw: {ex.Message}"); }
        if (op != null)
        {
            op.allowSceneActivation = true;
            float t = 0f;
            while (SceneManager.GetActiveScene().name != sceneName && t < _asyncWait)
            { t += Time.unscaledDeltaTime; yield return null; }
            if (SceneManager.GetActiveScene().name == sceneName) { done(true); yield break; }
        }

        // 3) Sync Single
        try
        {
            SceneManager.LoadScene(sceneName, LoadSceneMode.Single);
            if (SceneManager.GetActiveScene().name == sceneName) { done(true); yield break; }
        }
        catch (Exception ex) { Debug.LogWarning($"[Warp] Sync load threw: {ex.Message}"); }

        // 4) Additive fallback
        var old = SceneManager.GetActiveScene();
        AsyncOperation addOp = null;
        try { addOp = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive); }
        catch (Exception ex) { Debug.LogWarning($"[Warp] Additive threw: {ex.Message}"); }

        if (addOp != null)
        {
            float t = 0f;
            Scene tgt;
            while (!((tgt = SceneManager.GetSceneByName(sceneName)).IsValid() && tgt.isLoaded) && t < _additiveWait)
            { t += Time.unscaledDeltaTime; yield return null; }

            if (tgt.IsValid() && tgt.isLoaded && SceneManager.SetActiveScene(tgt))
            {
                try { SceneManager.UnloadSceneAsync(old); } catch { /* ignore */ }
                yield return null; yield return null; // settle
                if (SceneManager.GetActiveScene().name == sceneName) { done(true); yield break; }
            }
        }

        done(false);
    }

    private bool TryFindLoader()
    {
        if (_loaderMethod != null) return true;
        try
        {
            var asm = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "Assembly-CSharp");
            if (asm == null) return false;

            string[] preferred = { "BeginSceneTransition", "ChangeScene", "GoToScene" };
            string[] loadish   = { "LoadScene", "LoadLevel", "StartScene" };

            MethodInfo best = null; object instance = null;

            foreach (var type in asm.GetTypes())
            {
                if (type.Name.StartsWith("<")) continue;
                object singleton = GetSingleton(type);

                foreach (var m in type.GetMethods(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static))
                {
                    var pars = m.GetParameters();
                    if (pars.Length == 0 || pars[0].ParameterType != typeof(string)) continue;
                    string n = m.Name;
                    bool pref = preferred.Any(p => n.IndexOf(p, StringComparison.OrdinalIgnoreCase) >= 0);
                    bool load = loadish.Any(p => n.IndexOf(p, StringComparison.OrdinalIgnoreCase) >= 0);
                    if (!pref && !load) continue;

                    int score = pref ? 2 : 1;
                    int bestScore = best == null ? -1 :
                        (preferred.Any(p => best.Name.IndexOf(p, StringComparison.OrdinalIgnoreCase)>=0) ? 2 : 1);
                    if (best == null || score > bestScore) { best = m; instance = m.IsStatic? null : singleton; }
                }
            }

            if (best == null) return false;

            if (!best.IsStatic && instance == null)
            {
                var found = Resources.FindObjectsOfTypeAll(best.DeclaringType);
                if (found is { Length: > 0 }) instance = found[0];
            }

            _loaderMethod = best;
            _loaderInstance = best.IsStatic ? null : instance;
            return true;
        }
        catch { return false; }
    }

    private object GetSingleton(Type t)
    {
        try
        {
            var p = t.GetProperty("Instance", BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static);
            if (p != null && p.PropertyType == t) return p.GetValue(null);
            var f = t.GetField("Instance", BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static);
            if (f != null && f.FieldType == t) return f.GetValue(null);
        } catch { }
        return null;
    }

    private bool TryInvokeLoader(string scene)
    {
        try
        {
            if (_loaderMethod == null) return false;
            if (!_loaderMethod.IsStatic && _loaderInstance == null) return false;

            var pars = _loaderMethod.GetParameters();
            var args = new object[pars.Length];
            args[0] = scene;
            for (int i = 1; i < pars.Length; i++)
            {
                var pt = pars[i].ParameterType;
                args[i] = pt == typeof(bool) ? false :
                          pt == typeof(int) ? 0 :
                          pt == typeof(float) ? 0f :
                          pt == typeof(string) ? string.Empty :
                          pt.IsValueType ? Activator.CreateInstance(pt) : null;
            }

            _loaderMethod.Invoke(_loaderInstance, args);
            return true;
        }
        catch { return false; }
    }
}

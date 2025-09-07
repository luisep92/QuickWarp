using System;
using UnityEngine;

public sealed class PlayerLocator : IPlayerLocator
{
    public GameObject FindPlayer()
    {
        // First try by tag
        var byTag = GameObject.FindWithTag("Player");
        if (byTag) return byTag;

        // Active objects
        foreach (var t in UnityEngine.Object.FindObjectsByType<Transform>(
                     FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (!t) continue;
            var n = t.name;
            if (string.IsNullOrEmpty(n)) continue;
            if (n.Equals("Player", StringComparison.OrdinalIgnoreCase) ||
                n.IndexOf("Hornet", StringComparison.OrdinalIgnoreCase) >= 0)
                return t.gameObject;
        }

        // Inactive / hidden objects
        foreach (var t in Resources.FindObjectsOfTypeAll<Transform>())
        {
            if (!t) continue;
            var go = t.gameObject;
            var n = go.name;
            if (string.IsNullOrEmpty(n)) continue;
            if (n.Equals("Player", StringComparison.OrdinalIgnoreCase) ||
                n.IndexOf("Hornet", StringComparison.OrdinalIgnoreCase) >= 0 ||
                go.CompareTag("Player"))
                return go;
        }

        return null;
    }
}

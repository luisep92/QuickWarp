using System;
using System.Collections;
using UnityEngine;

public sealed class SceneSwitcher : ISceneSwitcher
{
    public IEnumerator SwitchTo(string sceneName, System.Action<bool> done)
    {
        var gameManager = GameManager.instance;
        if (gameManager == null) { done(false); yield break; }
        
        var currentScene = gameManager.GetSceneNameString();
        if (currentScene == sceneName) { done(true); yield break; }

        var sceneLoadInfo = new GameManager.SceneLoadInfo
        {
            SceneName = sceneName,
            EntryGateName = "top1",
            EntrySkip = true,
            PreventCameraFadeOut = false,
            WaitForSceneTransitionCameraFade = false
        };

        gameManager.BeginSceneTransition(sceneLoadInfo);
        
        float timeout = 10f;
        while (gameManager.GetSceneNameString() != sceneName && timeout > 0)
        {
            timeout -= Time.unscaledDeltaTime;
            yield return null;
        }
        
        while (gameManager.IsInSceneTransition && timeout > 0)
        {
            timeout -= Time.unscaledDeltaTime;
            yield return null;
        }
        
        done(gameManager.GetSceneNameString() == sceneName);
    }
}
using System;
using System.Collections;

namespace QuickWarp.Domain;

public interface ISceneSwitcher
{
    // Switches to target scene if different. Yields until done or timeout.
    IEnumerator SwitchTo(string sceneName, Action<bool> done);
}

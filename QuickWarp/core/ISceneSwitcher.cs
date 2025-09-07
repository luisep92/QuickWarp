using System.Collections;

public interface ISceneSwitcher
{
    // Switches to target scene if different. Yields until done or timeout.
    IEnumerator SwitchTo(string sceneName, System.Action<bool> done);
}

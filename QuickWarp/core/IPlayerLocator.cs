using UnityEngine;

public interface IPlayerLocator
{
    // Finds an active player if possible; can return inactive GO (caller checks activeInHierarchy)
    GameObject FindPlayer();
}

using UnityEngine;

/// <summary>
/// One place to spawn one-shot effects (blood flakes etc.), so hit scripts only hold a prefab reference.
/// The prefab destroys itself when done (ParticleSystem Stop Action = Destroy). If effects ever get spammy,
/// swap the Instantiate here for a pool without touching any caller.
/// </summary>
public static class VfxSpawner
{
    public static GameObject Spawn(GameObject prefab, Vector3 position, float scale = 1f)
    {
        if (prefab == null) return null;
        // Unrotated on purpose: the blood prefabs spray along world up ("mostly rises up")
        var go = Object.Instantiate(prefab, position, Quaternion.identity);
        if (!Mathf.Approximately(scale, 1f)) go.transform.localScale = Vector3.one * scale;
        return go;
    }
}

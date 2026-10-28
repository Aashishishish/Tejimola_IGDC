using UnityEngine;

/// <summary>
/// Anything the held weapon can hit: the Bottle Gourd and Lotus (KillAimTarget), the guard vines (VineHealth)...
/// InspectAndCutSystem only talks to this interface, so new hittable things don't need changes to the weapon.
/// </summary>
public interface IWeaponHittable
{
    /// <summary>False once the thing is defeated, so the weapon stops counting hits on it.</summary>
    bool AcceptsHits { get; }

    void RegisterHit(string weaponTag, Vector3 hitPoint, Vector3 swingDirection);
}

public static class WeaponHittables
{
    /// <summary>Finds the hittable a collider belongs to (itself, a parent, its Rigidbody, or a KillAimTarget extra collider).</summary>
    public static IWeaponHittable FindFor(Collider c)
    {
        if (c == null) return null;

        // KillAimTarget first: it also knows its "Additional Hit Colliders" that live outside its hierarchy
        KillAimTarget target = KillAimTarget.FindFor(c);
        if (target != null) return target;

        // GetComponentInParent works with interfaces, so any MonoBehaviour implementing IWeaponHittable is found
        IWeaponHittable h = c.GetComponentInParent<IWeaponHittable>();
        if (h == null && c.attachedRigidbody != null) h = c.attachedRigidbody.GetComponent<IWeaponHittable>();
        return h;
    }

    public static string NameOf(IWeaponHittable h) => h is Component comp ? comp.name : h?.ToString();
}

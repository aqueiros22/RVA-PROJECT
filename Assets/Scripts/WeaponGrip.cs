using UnityEngine;

// Put on a weapon prefab's root to give it its own pose in the hand,
// instead of the default grip set on CardWeaponManager.
// Tweak it in Play mode on "<weapon> (Equipped)" under hand_r, then copy Position/Rotation here.
public class WeaponGrip : MonoBehaviour
{
    // Weapon pose relative to the hand bone
    public Vector3 offset = Vector3.zero;
    public Vector3 rotation = new Vector3(0f, 0f, 90f);
}

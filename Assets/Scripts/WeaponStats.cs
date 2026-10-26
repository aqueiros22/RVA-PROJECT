using UnityEngine;

// Combat values of a weapon (see the Damage table in README.MD). Put on the weapon prefab's root.
public class WeaponStats : MonoBehaviour
{
    public WeaponClass weaponClass = WeaponClass.Light;
    // Damage dealt by an attack with this weapon, before the class multiplier
    public int baseDamage = 20;
    // Fraction of the damage that still gets through when this weapon blocks
    [Range(0f, 1f)] public float blockPassThrough = 0.4f;

    // Light beats Heavy, Heavy beats Defense, Defense beats Light
    public static WeaponClass StrongAgainst(WeaponClass weaponClass)
    {
        switch (weaponClass)
        {
            case WeaponClass.Light: return WeaponClass.Heavy;
            case WeaponClass.Heavy: return WeaponClass.Defense;
            default: return WeaponClass.Light;
        }
    }

    public static WeaponClass WeakAgainst(WeaponClass weaponClass)
    {
        // In a three-way cycle, the class that beats you is the one your counter beats
        return StrongAgainst(StrongAgainst(weaponClass));
    }
}

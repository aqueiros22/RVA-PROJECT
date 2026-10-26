using UnityEngine;
using UnityEngine.InputSystem;

// Editor-only arrows submit the current player's defense or attack through the same turn rules as the pen.
[RequireComponent(typeof(CardWeaponManager))]
public class KeyboardCombatInput : MonoBehaviour
{
#if UNITY_EDITOR
    private CardWeaponManager manager;

    void Awake()
    {
        manager = GetComponent<CardWeaponManager>();
    }

    void Update()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null)
        {
            return;
        }
        AttackDirection? direction = null;
        if (keyboard.upArrowKey.wasPressedThisFrame) direction = AttackDirection.Up;
        else if (keyboard.downArrowKey.wasPressedThisFrame) direction = AttackDirection.Down;
        else if (keyboard.leftArrowKey.wasPressedThisFrame) direction = AttackDirection.Left;
        else if (keyboard.rightArrowKey.wasPressedThisFrame) direction = AttackDirection.Right;
        if (direction.HasValue && manager.Duel != null) manager.Duel.SubmitGesture(direction.Value);
    }

#endif
}

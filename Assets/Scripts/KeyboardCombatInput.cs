using UnityEngine;
using UnityEngine.InputSystem;

// Editor-only keys to test the combat animations without the red-tip pen:
// 1/2 pick the player, arrows attack, Shift+arrows defend, K dies, R resets to Idle
[RequireComponent(typeof(CardWeaponManager))]
public class KeyboardCombatInput : MonoBehaviour
{
#if UNITY_EDITOR
    private CardWeaponManager manager;
    private int playerIndex;

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
        if (keyboard.digit1Key.wasPressedThisFrame) playerIndex = 0;
        if (keyboard.digit2Key.wasPressedThisFrame) playerIndex = 1;

        PlayerAvatar player = manager.GetPlayer(playerIndex);
        if (player == null)
        {
            return;
        }

        AttackDirection? direction = null;
        if (keyboard.upArrowKey.wasPressedThisFrame) direction = AttackDirection.Up;
        else if (keyboard.downArrowKey.wasPressedThisFrame) direction = AttackDirection.Down;
        else if (keyboard.leftArrowKey.wasPressedThisFrame) direction = AttackDirection.Left;
        else if (keyboard.rightArrowKey.wasPressedThisFrame) direction = AttackDirection.Right;

        if (direction.HasValue)
        {
            if (keyboard.shiftKey.isPressed)
            {
                player.PlayDefense(direction.Value);
            }
            else if (!player.TryAttack(direction.Value))
            {
                Debug.Log($"Jogador {playerIndex + 1}: equipa uma arma para atacar.");
            }
        }
        if (keyboard.kKey.wasPressedThisFrame) player.PlayDeath();
        if (keyboard.rKey.wasPressedThisFrame) player.GetComponent<Animator>().Rebind();
    }

    void OnGUI()
    {
        var style = new GUIStyle(GUI.skin.box) { fontSize = Mathf.Max(14, Screen.height / 60), alignment = TextAnchor.UpperLeft };
        GUI.Box(new Rect(Screen.width - 360, 12, 348, 110),
            $"Teclado - Jogador {playerIndex + 1} (1/2)\nSetas: atacar\nShift+setas: defender\nK: morrer   R: Idle", style);
    }
#endif
}

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Plays a round's resolved exchanges one after the other: wind-up, slowed swing, the defender's
// guard held for the whole swing, a short hit-stop when the blade connects, then a pause.
[RequireComponent(typeof(CardWeaponManager))]
public class ClashDirector : MonoBehaviour
{
    [Tooltip("Pause before each swing (seconds).")]
    public float windUp = 0.5f;
    [Tooltip("Swing speed in the clash; multiplies the weapon's own speed (heavy weapons are already slower).")]
    [Range(0.3f, 1f)] public float swingSpeed = 0.7f;
    [Tooltip("Point of the swing (0-1) where the blade connects.")]
    [Range(0.2f, 0.8f)] public float impactPoint = 0.45f;
    [Tooltip("Freeze on impact (seconds).")]
    public float hitStop = 0.12f;
    [Tooltip("Pause after each exchange (seconds).")]
    public float pauseAfter = 0.9f;
    [Tooltip("Stops waiting for an animation after this long (seconds).")]
    public float timeout = 6f;

    private CardWeaponManager manager;

    void Awake()
    {
        manager = GetComponent<CardWeaponManager>();
    }

    // onStart runs before each swing, onImpact when the blade connects
    public IEnumerator Play(IReadOnlyList<DuelMatch.Exchange> exchanges, Action<DuelMatch.Exchange> onStart, Action<DuelMatch.Exchange> onImpact)
    {
        foreach (var exchange in exchanges)
        {
            PlayerAvatar attacker = manager.GetAvatar(exchange.attacker);
            PlayerAvatar defender = manager.GetAvatar(exchange.Defender);
            onStart?.Invoke(exchange);
            yield return new WaitForSecondsRealtime(windUp);

            bool swung = attacker != null && attacker.TryAttack(exchange.direction, swingSpeed);
            GameAudio.Play("swing");
            // The guard goes up with the swing and stays up until it ends
            if (exchange.blocked && defender != null) defender.StartGuard(exchange.direction);

            float start = Time.unscaledTime;
            if (swung)
            {
                while (attacker != null && attacker.AttackProgress() < impactPoint && Time.unscaledTime - start < timeout)
                    yield return null;
            }
            else
            {
                yield return new WaitForSecondsRealtime(0.6f);
            }

            SetPaused(attacker, defender, true);
            onImpact?.Invoke(exchange);
            yield return new WaitForSecondsRealtime(hitStop);
            SetPaused(attacker, defender, false);

            bool knockedOut = exchange.defenderHealth == 0;
            if (knockedOut && defender != null)
            {
                defender.EndGuard();
                defender.PlayDeath();
            }

            // Let the swing finish (progress turns -1 once it blends back to Idle)
            start = Time.unscaledTime;
            while (swung && attacker != null && attacker.AttackProgress() >= 0f && attacker.AttackProgress() < 0.95f &&
                   Time.unscaledTime - start < timeout)
                yield return null;
            if (!knockedOut && defender != null) defender.EndGuard();
            yield return new WaitForSecondsRealtime(pauseAfter);
        }
    }

    private static void SetPaused(PlayerAvatar attacker, PlayerAvatar defender, bool paused)
    {
        if (attacker != null) attacker.SetAnimationPaused(paused);
        if (defender != null) defender.SetAnimationPaused(paused);
    }
}

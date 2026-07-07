using UnityEngine;
using System.Collections.Generic;

public class Hostile : MonoBehaviour
{
    [Tooltip("The amount of damage dealt")]
    [SerializeField] private int damage = 1;

    [Tooltip("Type of damage: 'ratio' (per touch) or 'interval' (over time)")]
    [SerializeField] private string damageType = "ratio";

    [Tooltip("Duration type: 'once' (destroy after triggering) or 'conserved' (persists)")]
    [SerializeField] private string durationType = "conserved";

    [Tooltip("Time interval between damage ticks if type is 'interval'")]
    [SerializeField] private float intervalTime = 1f;

    private Dictionary<PlayerController, float> _nextDamageTimes = new Dictionary<PlayerController, float>();
    private HashSet<PlayerController> _ratioDamaged = new HashSet<PlayerController>();
    private Dictionary<PlayerController, int> _colliderCount = new Dictionary<PlayerController, int>();
    private DamageMode _damageMode;
    private DurationMode _durationMode;

    private enum DamageMode
    {
        Ratio,
        Interval
    }

    private enum DurationMode
    {
        Conserved,
        Once
    }

    private void Awake()
    {
        RefreshModes();
    }

    private void OnValidate()
    {
        RefreshModes();
    }

    private void OnTriggerEnter(Collider other)
    {
        HandleEnter(other);
    }

    private void OnTriggerStay(Collider other)
    {
        HandleStay(other);
    }

    private void OnTriggerExit(Collider other)
    {
        HandleExit(other);
    }

    private void OnCollisionEnter(Collision collision)
    {
        HandleEnter(collision.collider);
    }

    private void OnCollisionStay(Collision collision)
    {
        HandleStay(collision.collider);
    }

    private void OnCollisionExit(Collision collision)
    {
        HandleExit(collision.collider);
    }

    private void HandleEnter(Collider other)
    {
        if (other.isTrigger) return; // Skip trigger colliders like interaction spheres
        
        PlayerController pc = other.GetComponentInParent<PlayerController>();
        if (pc == null) return;

        _colliderCount.TryGetValue(pc, out int colliderCount);
        colliderCount++;
        _colliderCount[pc] = colliderCount;

        if (colliderCount == 1) // First collider of this player entered
        {
            if (_damageMode == DamageMode.Interval)
            {
                pc.TakeDamage(damage, transform.position);
                _nextDamageTimes[pc] = Time.time + intervalTime;
                CheckOnce();
            }
            else if (_damageMode == DamageMode.Ratio)
            {
                if (!_ratioDamaged.Contains(pc))
                {
                    pc.TakeDamage(damage, transform.position);
                    _ratioDamaged.Add(pc);
                    CheckOnce();
                }
            }
        }
    }

    private void HandleStay(Collider other)
    {
        if (other.isTrigger) return;
        
        PlayerController pc = other.GetComponentInParent<PlayerController>();
        if (pc == null) return;

        if (_damageMode == DamageMode.Interval)
        {
            if (_nextDamageTimes.TryGetValue(pc, out float nextTime))
            {
                if (Time.time >= nextTime)
                {
                    pc.TakeDamage(damage, transform.position);
                    _nextDamageTimes[pc] = Time.time + intervalTime;
                    CheckOnce();
                }
            }
        }
    }

    private void HandleExit(Collider other)
    {
        if (other.isTrigger) return;
        
        PlayerController pc = other.GetComponentInParent<PlayerController>();
        if (pc == null) return;

        if (_colliderCount.TryGetValue(pc, out int colliderCount))
        {
            colliderCount--;
            if (colliderCount <= 0)
            {
                _colliderCount.Remove(pc);
                _nextDamageTimes.Remove(pc);
                _ratioDamaged.Remove(pc);
            }
            else
            {
                _colliderCount[pc] = colliderCount;
            }
        }
    }

    private void CheckOnce()
    {
        if (_durationMode == DurationMode.Once)
        {
            if (Application.isPlaying) Destroy(gameObject);
            else UnityEngine.Object.DestroyImmediate(gameObject);
        }
    }

    private void RefreshModes()
    {
        _damageMode = damageType == "interval" ? DamageMode.Interval : DamageMode.Ratio;
        _durationMode = durationType == "once" ? DurationMode.Once : DurationMode.Conserved;
    }
}

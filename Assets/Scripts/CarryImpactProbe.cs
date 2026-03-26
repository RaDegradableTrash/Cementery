using UnityEngine;

/// <summary>
/// Records the maximum collision impulse seen on this object between reads.
/// InteractionSystem uses this to break carry on strong impacts.
/// </summary>
public class CarryImpactProbe : MonoBehaviour
{
    private float _maxImpulse;

    public void ResetImpulse()
    {
        _maxImpulse = 0f;
    }

    public float ConsumeMaxImpulse()
    {
        float value = _maxImpulse;
        _maxImpulse = 0f;
        return value;
    }

    void OnCollisionEnter(Collision collision)
    {
        Record(collision);
    }

    void OnCollisionStay(Collision collision)
    {
        Record(collision);
    }

    void Record(Collision collision)
    {
        float impulse = collision.impulse.magnitude;
        if (impulse > _maxImpulse)
            _maxImpulse = impulse;
    }
}

using UnityEngine;

public class ReadingLightSystem : MonoBehaviour
{
    [SerializeField] private StartProcedure startProcedure;

    private readonly bool[] states = new bool[2];

    public event System.Action OnStateChanged;

    public bool HasPower => startProcedure == null || startProcedure.HasAnyBatteryOn();

    private void Awake()
    {
        if (startProcedure == null)
        {
            startProcedure = FindObjectOfType<StartProcedure>();
        }
    }

    public bool IsLightOn(int index)
    {
        if (index < 0 || index >= states.Length)
        {
            return false;
        }
        return states[index];
    }

    public void ToggleLight(int index)
    {
        if (index < 0 || index >= states.Length)
        {
            return;
        }

        states[index] = !states[index];
        OnStateChanged?.Invoke();
    }
}

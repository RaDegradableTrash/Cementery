using System.Collections;
using UnityEngine;

public class StartProcedure : MonoBehaviour
{
    [SerializeField] private CarControl carControl;
    [SerializeField] private float shutdownDelaySeconds = 1f;

    private readonly bool[] batteries = new bool[4];
    private bool leftPumpOn;
    private bool rightPumpOn;
    private bool engineOn;

    public event System.Action OnStateChanged;

    public bool EngineOn => engineOn;

    private void Awake()
    {
        if (carControl == null)
        {
            carControl = FindObjectOfType<CarControl>();
        }

        if (carControl != null)
        {
            carControl.SetEngineOn(engineOn);
        }
    }

    public bool IsBatteryOn(int index)
    {
        if (index < 0 || index >= batteries.Length)
        {
            return false;
        }

        return batteries[index];
    }

    public bool IsLeftPumpOn()
    {
        return leftPumpOn;
    }

    public bool IsRightPumpOn()
    {
        return rightPumpOn;
    }

    public bool HasAnyBatteryOn()
    {
        return HasAnyBatteryInternal();
    }

    public bool HasAnyPumpOn()
    {
        return leftPumpOn || rightPumpOn;
    }

    public bool CanStartEngine()
    {
        return HasAnyBatteryOn() && (leftPumpOn || rightPumpOn);
    }

    public void ToggleBattery(int index)
    {
        if (index < 0 || index >= batteries.Length)
        {
            return;
        }

        batteries[index] = !batteries[index];
        OnStateChanged?.Invoke();
    }

    public void ToggleLeftPump()
    {
        leftPumpOn = !leftPumpOn;
        OnStateChanged?.Invoke();
    }

    public void ToggleRightPump()
    {
        rightPumpOn = !rightPumpOn;
        OnStateChanged?.Invoke();
    }

    public void ToggleEngine()
    {
        if (engineOn)
        {
            StartCoroutine(ShutdownRoutine());
            return;
        }

        if (!CanStartEngine())
        {
            return;
        }

        engineOn = true;
        if (carControl != null)
        {
            carControl.SetEngineOn(true);
        }
        OnStateChanged?.Invoke();
    }

    private IEnumerator ShutdownRoutine()
    {
        if (carControl != null)
        {
            carControl.SetGear(CarControl.GearMode.Park);
        }

        yield return new WaitForSeconds(shutdownDelaySeconds);
        engineOn = false;
        if (carControl != null)
        {
            carControl.SetEngineOn(false);
        }
        OnStateChanged?.Invoke();
    }

    private bool HasAnyBatteryInternal()
    {
        for (int i = 0; i < batteries.Length; i++)
        {
            if (batteries[i])
            {
                return true;
            }
        }
        return false;
    }
}

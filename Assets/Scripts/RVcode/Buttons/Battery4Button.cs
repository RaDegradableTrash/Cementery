using UnityEngine;

public class Battery4Button : StartProcedureButtonBase
{
    private void Reset()
    {
        SetIdleGlowDefaults();
    }

    protected override void Awake()
    {
        SetIdleGlowDefaults();
        base.Awake();
    }

    protected override bool IsOn(StartProcedure procedure) => procedure != null && procedure.IsBatteryOn(3);

    protected override void Toggle(StartProcedure procedure)
    {
        procedure.ToggleBattery(3);
    }

    private void SetIdleGlowDefaults()
    {
        SetIdleGlow(true, new Color(0.95f, 0.95f, 0.95f, 1f), -5.5f);
    }
}

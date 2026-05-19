using UnityEngine;

public class LPumpButton : StartProcedureButtonBase
{
    protected override bool IsOn(StartProcedure procedure) => procedure != null && procedure.IsLeftPumpOn();

    protected override void Toggle(StartProcedure procedure)
    {
        procedure.ToggleLeftPump();
    }
}

using UnityEngine;

public class RPumpButton : StartProcedureButtonBase
{
    protected override bool IsOn(StartProcedure procedure) => procedure != null && procedure.IsRightPumpOn();

    protected override void Toggle(StartProcedure procedure)
    {
        procedure.ToggleRightPump();
    }
}

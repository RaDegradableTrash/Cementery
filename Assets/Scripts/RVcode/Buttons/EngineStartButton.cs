using UnityEngine;

public class EngineStartButton : StartProcedureButtonBase
{
    protected override bool IsOn(StartProcedure procedure) => procedure != null && procedure.EngineOn;

    protected override void Toggle(StartProcedure procedure)
    {
        procedure.ToggleEngine();
    }
}

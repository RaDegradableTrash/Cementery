using UnityEngine;

public class NButton : GearButtonBase
{
    protected override CarControl.GearMode Gear => CarControl.GearMode.Neutral;
}

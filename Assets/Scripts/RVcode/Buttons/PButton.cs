using UnityEngine;

public class PButton : GearButtonBase
{
    protected override CarControl.GearMode Gear => CarControl.GearMode.Park;
}

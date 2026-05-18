using UnityEngine;

public class RButton : GearButtonBase
{
    protected override CarControl.GearMode Gear => CarControl.GearMode.Reverse;
}

using UnityEngine;

public class SButton : GearButtonBase
{
    protected override CarControl.GearMode Gear => CarControl.GearMode.Sport;
}

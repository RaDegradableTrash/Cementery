using UnityEngine;

public class DButton : GearButtonBase
{
    protected override CarControl.GearMode Gear => CarControl.GearMode.Drive;
}

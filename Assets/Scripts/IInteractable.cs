using UnityEngine;

/// <summary>
/// Implement this interface on any object that the player can interact with (E key).
/// </summary>
public interface IInteractable
{
    /// <summary>Prompt text shown in the HUD, e.g. "Pick up Wood"</summary>
    string InteractPrompt { get; }

    /// <summary>Called when the player presses the interact key while looking at this object.</summary>
    void Interact(GameObject interactor);
}

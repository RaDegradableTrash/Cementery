using UnityEngine;

public enum InteractionFeedbackState
{
    Focus,
    Active,
    Completed
}

public class InteractionFeedback : MonoBehaviour
{
    [SerializeField] private Color focusColor = new Color(0.25f, 0.75f, 1f, 1f);
    [SerializeField] private Color activeColor = new Color(1f, 0.85f, 0.25f, 1f);
    [SerializeField] private Color completedColor = new Color(0.35f, 1f, 0.45f, 1f);
    [SerializeField] private float completedHoldSeconds = 0.18f;

    private Renderer[] renderers;
    private readonly System.Collections.Generic.Dictionary<Renderer, Color> originalColors =
        new System.Collections.Generic.Dictionary<Renderer, Color>();
    private float clearAtTime = -1f;

    public static InteractionFeedback GetOrCreate(Component target)
    {
        if (target == null)
        {
            return null;
        }

        InteractionFeedback feedback = target.GetComponent<InteractionFeedback>();
        if (feedback == null)
        {
            feedback = target.gameObject.AddComponent<InteractionFeedback>();
        }

        return feedback;
    }

    public void SetState(InteractionFeedbackState state, string message)
    {
        EnsureRendererCache();

        Color color = ResolveColor(state);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null || renderer.sharedMaterial == null || !renderer.material.HasProperty("_BaseColor"))
            {
                continue;
            }

            if (!originalColors.ContainsKey(renderer))
            {
                originalColors[renderer] = renderer.material.GetColor("_BaseColor");
            }

            renderer.material.SetColor("_BaseColor", color);
        }

        clearAtTime = state == InteractionFeedbackState.Completed
            ? Time.time + completedHoldSeconds
            : -1f;
    }

    public void Clear()
    {
        foreach (var kv in originalColors)
        {
            if (kv.Key != null && kv.Key.sharedMaterial != null && kv.Key.material.HasProperty("_BaseColor"))
            {
                kv.Key.material.SetColor("_BaseColor", kv.Value);
            }
        }

        originalColors.Clear();
        clearAtTime = -1f;
    }

    private void Update()
    {
        if (clearAtTime > 0f && Time.time >= clearAtTime)
        {
            Clear();
        }
    }

    private void EnsureRendererCache()
    {
        if (renderers == null)
        {
            renderers = GetComponentsInChildren<Renderer>(true);
        }
    }

    private Color ResolveColor(InteractionFeedbackState state)
    {
        switch (state)
        {
            case InteractionFeedbackState.Active:
                return activeColor;
            case InteractionFeedbackState.Completed:
                return completedColor;
            default:
                return focusColor;
        }
    }
}

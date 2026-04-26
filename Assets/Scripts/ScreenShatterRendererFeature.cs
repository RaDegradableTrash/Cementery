using UnityEngine.Rendering.Universal;

// 这是一个空壳类，仅用于修复由于我们删除了旧脚本导致 URP_Performance_Renderer 找不到引用而报出的黄字警告。
// 你可以随时在 URP_Performance_Renderer 中将 Missing 的 Feature 删掉，然后再把这个文件删掉。
public class ScreenShatterRendererFeature : ScriptableRendererFeature
{
    public override void Create() {}
    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData) {}
}

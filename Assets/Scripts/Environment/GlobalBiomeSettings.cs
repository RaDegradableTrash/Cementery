using UnityEngine;

namespace EnvironmentSystem
{
    [CreateAssetMenu(fileName = "GlobalBiomeSettings", menuName = "Environment/Global Biome Settings")]
    public class GlobalBiomeSettings : ScriptableObject
    {
        public int globalSeed = 42;
        public float noiseScale = 800f;
        public Gradient biomeGradient;

        public void ResetToDefault()
        {
            biomeGradient = new Gradient();
            GradientColorKey[] gck = new GradientColorKey[7];
            ColorUtility.TryParseHtmlString("#5E716A", out Color c0);
            ColorUtility.TryParseHtmlString("#78866B", out Color c1);
            ColorUtility.TryParseHtmlString("#88937B", out Color c2);
            ColorUtility.TryParseHtmlString("#CCD67F", out Color c3);
            ColorUtility.TryParseHtmlString("#F5F5DC", out Color c4);
            ColorUtility.TryParseHtmlString("#E8E1D5", out Color c5);
            ColorUtility.TryParseHtmlString("#CEB59E", out Color c6);
            
            gck[0] = new GradientColorKey(c0, 0.00f);
            gck[1] = new GradientColorKey(c1, 0.16f);
            gck[2] = new GradientColorKey(c2, 0.33f);
            gck[3] = new GradientColorKey(c3, 0.50f);
            gck[4] = new GradientColorKey(c4, 0.66f);
            gck[5] = new GradientColorKey(c5, 0.83f);
            gck[6] = new GradientColorKey(c6, 1.00f);
            
            GradientAlphaKey[] gak = new GradientAlphaKey[2];
            gak[0] = new GradientAlphaKey(1.0f, 0.0f);
            gak[1] = new GradientAlphaKey(1.0f, 1.0f);
            
            biomeGradient.SetKeys(gck, gak);
        }
    }
}

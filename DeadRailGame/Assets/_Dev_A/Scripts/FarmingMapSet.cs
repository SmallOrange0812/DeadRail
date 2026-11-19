using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Map indices come from authority-owned route data, including for late joiners.</summary>
public sealed class FarmingMapSet : MonoBehaviour
{
    public GameObject[] variants;
    private int presentedIndex = -2;
    private bool underground;
    private bool savedFog;
    private Color savedFogColor;
    private float savedFogDensity;
    private FogMode savedFogMode;
    private AmbientMode savedAmbientMode;
    private Color savedAmbientLight;
    private float savedAmbientIntensity;

    private void Awake()
    {
        savedFog = RenderSettings.fog;
        savedFogColor = RenderSettings.fogColor;
        savedFogDensity = RenderSettings.fogDensity;
        savedFogMode = RenderSettings.fogMode;
        savedAmbientMode = RenderSettings.ambientMode;
        savedAmbientLight = RenderSettings.ambientLight;
        savedAmbientIntensity = RenderSettings.ambientIntensity;
    }

    public void Present(int mapIndex, bool visible)
    {
        if (presentedIndex != mapIndex)
        {
            for (int i = 0; i < variants.Length; i++)
                if (variants[i] != null) variants[i].SetActive(i == mapIndex);
            presentedIndex = mapIndex;
        }
        bool nextUnderground = visible && mapIndex >= MapNodeManager.NormalMapCount;
        if (nextUnderground == underground) return;
        underground = nextUnderground;
        if (underground)
        {
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = 0.025f;
            RenderSettings.fogColor = new Color(0.025f, 0.035f, 0.045f);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.18f, 0.2f, 0.23f);
            RenderSettings.ambientIntensity = 0.7f;
        }
        else RestoreLighting();
    }

    private void RestoreLighting()
    {
        RenderSettings.fog = savedFog;
        RenderSettings.fogColor = savedFogColor;
        RenderSettings.fogDensity = savedFogDensity;
        RenderSettings.fogMode = savedFogMode;
        RenderSettings.ambientMode = savedAmbientMode;
        RenderSettings.ambientLight = savedAmbientLight;
        RenderSettings.ambientIntensity = savedAmbientIntensity;
    }

    private void OnDestroy()
    {
        if (underground) RestoreLighting();
    }
}

using UnityEngine;
using UnityEngine.UI;

/// <summary>Blurs the live 3D depot, without replacing it with the supplied sunset screenshot.</summary>
public sealed class FrontendBackdrop : MonoBehaviour
{
    public Camera backgroundCamera;
    public RawImage blurredView;
    public Shader blurShader;
    private RenderTexture texture;
    private Material material;
    private Camera outputCamera;
    private float blurRadius = 4f;

    public void SetBlurRadius(float radius)
    {
        blurRadius = radius;
        if (material != null) material.SetFloat("_Radius", radius);
    }

    public void SetBlurred(bool blurred)
    {
        blurredView.gameObject.SetActive(blurred);
        if (!blurred)
        {
            if (backgroundCamera != null) backgroundCamera.targetTexture = null;
            if (outputCamera != null) outputCamera.enabled = false;
            return;
        }
        // During network scene loading the last rendered depot remains visible in the loading overlay.
        if (backgroundCamera == null) return;
        if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
        if (outputCamera == null)
        {
            outputCamera = new GameObject("Menu output camera", typeof(Camera)).GetComponent<Camera>();
            outputCamera.transform.SetParent(transform, false);
            outputCamera.cullingMask = 0;
            outputCamera.clearFlags = CameraClearFlags.SolidColor;
            outputCamera.backgroundColor = Color.black;
            outputCamera.depth = -100;
        }
        outputCamera.enabled = true;
        int width = Mathf.Clamp(Screen.width, 640, 2560);
        int height = Mathf.Clamp(Screen.height, 360, 1440);
        if (texture == null || texture.width != width || texture.height != height)
        {
            ReleaseTexture();
            texture = new RenderTexture(width, height, 24) { name = "Frontend depot view", filterMode = FilterMode.Bilinear };
            texture.Create();
        }
        if (material == null)
        {
            material = new Material(blurShader) { name = "Frontend blur instance" };
            blurredView.material = material;
        }
        material.SetFloat("_Radius", blurRadius);
        backgroundCamera.targetTexture = texture;
        blurredView.texture = texture;
    }

    private void LateUpdate()
    {
        if (texture != null && blurredView.gameObject.activeSelf && backgroundCamera != null &&
            (texture.width != Mathf.Clamp(Screen.width, 640, 2560) || texture.height != Mathf.Clamp(Screen.height, 360, 1440)))
            SetBlurred(true);
    }

    private void ReleaseTexture()
    {
        if (texture == null) return;
        if (backgroundCamera != null && backgroundCamera.targetTexture == texture)
            backgroundCamera.targetTexture = null;
        texture.Release();
        Destroy(texture);
        texture = null;
    }

    private void OnDestroy()
    {
        ReleaseTexture();
        if (material != null) Destroy(material);
    }
}

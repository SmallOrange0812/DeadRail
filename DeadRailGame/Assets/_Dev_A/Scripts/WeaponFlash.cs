using UnityEngine;

internal sealed class WeaponFlash : MonoBehaviour
{
    private static readonly Color32 Core = new Color32(255, 255, 255, 255);
    private static readonly Color32 InnerFlame = new Color32(255, 243, 176, 255);
    private static readonly Color32 Amber = new Color32(255, 158, 0, 255);
    private static readonly Color32 Vermilion = new Color32(230, 57, 0, 235);
    private static readonly Color32 Smoke = new Color32(43, 43, 43, 140);
    private ParticleSystem particles;
    private Material particleMaterial;
    private float scale;

    internal static WeaponFlash Attach(GameObject weapon, bool rifle)
    {
        var tip = new GameObject("Muzzle flash");
        tip.transform.SetParent(weapon.transform, false);
        // Centre of the front mesh rim, in the supplied M4_8 and M1911 prefab coordinates.
        tip.transform.localPosition = rifle ? new Vector3(0f, .05846f, -.33835f) : new Vector3(0f, .02511f, -.15073f);
        tip.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        var flash = tip.AddComponent<WeaponFlash>();
        flash.scale = rifle ? 1f : .74f;
        flash.particles = tip.AddComponent<ParticleSystem>();
        flash.particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = flash.particles.main;
        main.playOnAwake = false;
        main.loop = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 64;
        var emission = flash.particles.emission;
        emission.enabled = false;
        var shape = flash.particles.shape;
        shape.enabled = false;
        var fade = new Gradient();
        fade.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
            new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(.9f, .3f), new GradientAlphaKey(0, 1) });
        var color = flash.particles.colorOverLifetime;
        color.enabled = true;
        color.color = fade;
        flash.particleMaterial = new Material(Resources.Load<Shader>("DeadRailMuzzlePuff"));
        tip.GetComponent<ParticleSystemRenderer>().sharedMaterial = flash.particleMaterial;
        return flash;
    }

    internal void Fire()
    {
        if (!particles.isPlaying) particles.Play(false);
        Vector3 forward = transform.forward;
        Puff(Core, .012f, .074f, .045f, forward * .12f);
        Puff(InnerFlame, .045f, .10f, .055f, forward * .4f);
        Puff(Amber, .092f, .097f, .065f, forward * .65f);
        for (int i = 0; i < 4; i++)
        {
            float angle = i * Mathf.PI * .5f + Random.Range(-.35f, .35f);
            Vector3 radial = transform.right * Mathf.Cos(angle) + transform.up * Mathf.Sin(angle);
            Puff(Vermilion, .13f, .055f, .08f, forward + radial * .85f);
        }
        Puff(Smoke, .16f, .13f, .38f, forward * .3f + Vector3.up * .45f);
    }

    private void Puff(Color32 color, float distance, float size, float lifetime, Vector3 velocity)
    {
        particles.Emit(new ParticleSystem.EmitParams {
            position = transform.position + transform.forward * (distance * scale),
            velocity = velocity * scale, startColor = color, startSize = size * scale,
            startLifetime = lifetime, rotation = Random.Range(0f, 360f)
        }, 1);
    }

    private void OnDestroy()
    {
        if (particleMaterial != null) Destroy(particleMaterial);
    }
}

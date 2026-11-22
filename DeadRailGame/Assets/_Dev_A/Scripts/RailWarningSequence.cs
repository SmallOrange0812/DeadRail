using System;
using UnityEngine;

public sealed class RailWarningSequence : MonoBehaviour
{
    [Serializable]
    public struct Lamp
    {
        public Renderer lens;
        public Light spill;
        [Min(0)] public int step;
    }

    [SerializeField, Min(2)] private int stepCount = 4;
    [SerializeField, Min(.2f)] private float secondsPerStep = .46f;
    [SerializeField] private Lamp[] lamps = Array.Empty<Lamp>();

    private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
    private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
    private static readonly Color LensOn = new Color(.68f, .037f, .021f);
    private static readonly Color LensOff = new Color(.16f, .033f, .023f);
    private static readonly Color EmissionOn = new Color(2.35f, .048f, .014f);
    private static readonly Color EmissionOff = new Color(.022f, .0015f, 0f);

    private MaterialPropertyBlock properties;
    private int displayedStep = -1;

    private void OnEnable() => displayedStep = -1;

    private void Update()
    {
        RailRouteLoop route = RailRouteLoop.Instance;
        PresentAt(route != null && route.Ready ? route.Runner.SimulationTime : Time.timeAsDouble);
    }

    public void PresentAt(double time)
    {
        int step = (int)Math.Floor(Math.Max(0d, time) / secondsPerStep) % stepCount;
        if (step == displayedStep) return;
        displayedStep = step;
        properties ??= new MaterialPropertyBlock();
        foreach (Lamp lamp in lamps)
        {
            bool on = lamp.step == step;
            if (lamp.lens != null)
            {
                lamp.lens.GetPropertyBlock(properties);
                properties.SetColor(BaseColor, on ? LensOn : LensOff);
                properties.SetColor(EmissionColor, on ? EmissionOn : EmissionOff);
                lamp.lens.SetPropertyBlock(properties);
            }
            if (lamp.spill != null) lamp.spill.enabled = on;
        }
    }

#if UNITY_EDITOR
    public void Configure(int steps, Lamp[] orderedLamps)
    {
        stepCount = steps;
        lamps = orderedLamps;
        displayedStep = -1;
    }
#endif
}

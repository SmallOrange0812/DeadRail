using UnityEngine;
using Fusion;

public class FlashlightToggle : NetworkBehaviour
{
    private Light spotlight;
    private PlayerMovement player;
    private EquipmentPivotController equipment;

    [Networked] public NetworkBool IsLightOn { get; set; }

    public override void Spawned()
    {
        spotlight = GetComponentInChildren<Light>(true);
        player = GetComponent<PlayerMovement>();
        equipment = GetComponent<EquipmentPivotController>();
        if (HasStateAuthority) IsLightOn = false;
    }

    public override void FixedUpdateNetwork()
    {
        if (HasStateAuthority && IsLightOn && (UsesSearchlight || !HasBattery))
            IsLightOn = false;
    }

    private bool UsesSearchlight => equipment != null
        && equipment.CurrentMode == EquipmentPivotController.EquipmentMode.P2_Searchlight;

    private static bool HasBattery
    {
        get
        {
            TreadmillManager train = TreadmillManager.Instance;
            return train == null || !train.HasSpawned || train.Battery > 0f;
        }
    }

    internal void SetPersonalLight(bool on)
    {
        if (HasStateAuthority) IsLightOn = on && !UsesSearchlight && HasBattery;
    }

    private void LateUpdate()
    {
        if (Object == null || !Object.IsValid || spotlight == null || player == null) return;
        spotlight.enabled = IsLightOn && !UsesSearchlight;
        if (!spotlight.enabled) return;

        Camera camera = player.fpsCamera;
        if (player.IsLocalPlayer && camera != null && camera.isActiveAndEnabled)
            spotlight.transform.rotation = camera.transform.rotation;
        else
            spotlight.transform.rotation = player.transform.rotation * Quaternion.Euler(player.CameraPitch, 0f, 0f);
    }
}

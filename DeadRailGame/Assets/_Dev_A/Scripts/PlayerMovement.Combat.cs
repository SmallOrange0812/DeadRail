using Fusion;
using UnityEngine;

public partial class PlayerMovement
{
    private const int MaxHealth = 100;
    private const float MaxStamina = 100f;
    private const float ReviveRange = 2.5f;

    [Networked] public int Health { get; private set; }
    [Networked] public float Stamina { get; private set; }
    [Networked] private int CombatSlot { get; set; }
    [Networked] public TrainRole AssignedRoles { get; private set; }
    [Networked] private int Magazine { get; set; }
    [Networked] private NetworkBool FlashlightSelected { get; set; }
    [Networked] private TickTimer ShotCooldown { get; set; }
    [Networked] private TickTimer ReloadTimer { get; set; }
    [Networked] private int WeakPointHits { get; set; }

    private GameObject weaponVisual;
    private EquipmentPivotController combatEquipment;
    private FlashlightToggle personalLight;
    private int shownWeakPointHits;
    private float weakPointFlashUntil;
    private float nextShotInput;

    public bool IsDowned => Health == 0;
    internal bool CanUsePersonalEquipment => combatEquipment == null ||
        combatEquipment.CurrentMode == EquipmentPivotController.EquipmentMode.None;
    internal bool HasRole(TrainRole role) =>
        role != TrainRole.None && Object != null && Object.IsValid &&
        (Runner.GameMode == GameMode.Single || (AssignedRoles & role) == role);

    internal static bool CanPlayerUseRole(NetworkRunner runner, RpcInfo info, TrainRole role)
    {
        if (runner.GameMode == GameMode.Single) return true;
        PlayerRef sender = info.Source == PlayerRef.None ? runner.LocalPlayer : info.Source;
        return runner.TryGetPlayerObject(sender, out NetworkObject player) &&
            player.TryGetComponent(out PlayerMovement movement) && !movement.IsDowned && movement.HasRole(role);
    }

    private bool IsRifle => (AssignedRoles & TrainRole.Gunner) != 0;
    private int MagazineCapacity => IsRifle ? 30 : 7;
    private bool CanFight => !IsDowned && !IsClimbingLadder && CanUsePersonalEquipment &&
        (RailRouteLoop.Instance == null || !RailRouteLoop.Instance.Ready || !RailRouteLoop.Instance.IsTransition) &&
        (TreadmillManager.Instance == null || !TreadmillManager.Instance.HasSpawned ||
         TreadmillManager.Instance.CrankUser != Object.InputAuthority ||
         TreadmillManager.Instance.CrankUser == PlayerRef.None);

    private void InitializeCombat()
    {
        combatEquipment = GetComponent<EquipmentPivotController>();
        personalLight = GetComponent<FlashlightToggle>();
        if (!HasStateAuthority) return;
        Health = MaxHealth;
        Stamina = MaxStamina;
    }

    internal void AssignCombatSlot(int slot, TrainRole roles)
    {
        if (!HasStateAuthority || CombatSlot != 0) return;
        CombatSlot = slot;
        AssignedRoles = roles;
        Magazine = MagazineCapacity;
        InventoryAmmunition += MagazineCapacity * 3;
    }

    internal void ResetPlayerForRun()
    {
        if (!HasStateAuthority) return;
        if (_isClimbingLadder) ReleaseLadder();
        _velocityY = 0f;
        CameraPitch = 0f;
        Health = MaxHealth;
        Stamina = MaxStamina;
        InventoryMedicalKits = InventoryRepairKits = 0;
        Magazine = CombatSlot != 0 ? MagazineCapacity : 0;
        InventoryAmmunition = Magazine * 3;
        FlashlightSelected = false;
        ShotCooldown = ReloadTimer = default;
        if (personalLight != null) personalLight.SetPersonalLight(false);
    }

    private float UpdateSprint(PlayerControlInput controls)
    {
        bool sprint = controls.SprintHeld && controls.Move.sqrMagnitude > 0.01f && Stamina > 0f;
        float change = sprint ? -24f : controls.SprintHeld && controls.Move.sqrMagnitude > 0.01f ? 0f : 18f;
        Stamina = Mathf.Clamp(Stamina + change * Runner.DeltaTime, 0f, MaxStamina);
        return sprint ? moveSpeed * 1.55f : moveSpeed;
    }

    internal void ApplyZombieDamage(int damage)
    {
        if (!HasStateAuthority || IsDowned) return;
        Health = Mathf.Max(0, Health - damage);
        if (!IsDowned) return;
        ReloadTimer = default;
        if (combatEquipment != null && combatEquipment.CurrentMode != EquipmentPivotController.EquipmentMode.None)
            combatEquipment.TryToggleMode(combatEquipment.CurrentMode);
        if (personalLight != null) personalLight.SetPersonalLight(false);
    }

    private PlayerMovement FindReviveTarget()
    {
        PlayerMovement target = null;
        float nearest = ReviveRange * ReviveRange;
        foreach (PlayerRef player in Runner.ActivePlayers)
        {
            if (!Runner.TryGetPlayerObject(player, out NetworkObject other) || other == Object ||
                !other.TryGetComponent(out PlayerMovement movement) || !movement.IsDowned) continue;
            float distance = (other.transform.position - transform.position).sqrMagnitude;
            if (distance > nearest) continue;
            target = movement;
            nearest = distance;
        }
        return target;
    }

    private bool TryRequestRevive()
    {
        PlayerMovement target = FindReviveTarget();
        if (target == null) return false;
        Rpc_Revive(target.Object.Id);
        return true;
    }

    [Rpc(RpcSources.InputAuthority | RpcSources.StateAuthority, RpcTargets.StateAuthority)]
    private void Rpc_Revive(NetworkId targetId)
    {
        if (!CanFight || !Runner.TryFindObject(targetId, out NetworkObject target) || target == Object ||
            !target.TryGetComponent(out PlayerMovement movement) || !movement.HasStateAuthority ||
            !movement.IsDowned || (target.transform.position - transform.position).sqrMagnitude > ReviveRange * ReviveRange)
            return;
        movement.Health = 40;
    }

    private void Update()
    {
        if (GamePauseMenu.BlocksGameplayInput || Object == null || !Object.IsValid || !IsLocalPlayer || CombatSlot == 0) return;
        if (IsDowned && TerminalUIManager.Instance != null && TerminalUIManager.Instance.IsUiOpen)
            TerminalUIManager.Instance.CloseAllTerminals();
        if (!CanFight || isUsingTerminal ||
            (TerminalUIManager.Instance != null && TerminalUIManager.Instance.BlocksPlayerInput)) return;

        if (Input.GetKeyDown(KeyCode.Alpha1))
            Rpc_SelectEquipment(false);
        else if (Input.GetKeyDown(KeyCode.Alpha2))
            Rpc_SelectEquipment(true);
        else if (!FlashlightSelected && Input.GetKeyDown(KeyCode.R))
            Rpc_Reload();
        else if (!FlashlightSelected && Magazine > 0 && !ReloadTimer.IsRunning &&
                 (IsRifle ? Input.GetMouseButton(0) : Input.GetMouseButtonDown(0)) &&
                 Time.unscaledTime >= nextShotInput)
        {
            nextShotInput = Time.unscaledTime + (IsRifle ? 0.115f : 0.27f);
            Rpc_Fire(CameraPitch);
        }
    }

    [Rpc(RpcSources.InputAuthority | RpcSources.StateAuthority, RpcTargets.StateAuthority)]
    private void Rpc_SelectEquipment(bool flashlight)
    {
        if (!CanFight) return;
        if (flashlight && !FlashlightSelected) ReloadTimer = default;
        FlashlightSelected = flashlight;
        if (personalLight != null) personalLight.SetPersonalLight(flashlight && !personalLight.IsLightOn);
    }

    [Rpc(RpcSources.InputAuthority | RpcSources.StateAuthority, RpcTargets.StateAuthority)]
    private void Rpc_Reload()
    {
        if (!CanFight || FlashlightSelected || Magazine >= MagazineCapacity ||
            InventoryAmmunition == 0 || ReloadTimer.IsRunning) return;
        ReloadTimer = TickTimer.CreateFromSeconds(Runner, 1.25f);
    }

    private void AdvanceReload()
    {
        if (!ReloadTimer.IsRunning) return;
        if (!CanFight || FlashlightSelected)
        {
            ReloadTimer = default;
            return;
        }
        if (!ReloadTimer.Expired(Runner)) return;
        int rounds = Mathf.Min(MagazineCapacity - Magazine, InventoryAmmunition);
        Magazine += rounds;
        InventoryAmmunition -= rounds;
        ReloadTimer = default;
    }

    [Rpc(RpcSources.InputAuthority | RpcSources.StateAuthority, RpcTargets.StateAuthority)]
    private void Rpc_Fire(float aimPitch)
    {
        if (!CanFight || FlashlightSelected || Magazine <= 0 || ReloadTimer.IsRunning ||
            !ShotCooldown.ExpiredOrNotRunning(Runner) || !float.IsFinite(aimPitch)) return;
        Magazine--;
        ShotCooldown = TickTimer.CreateFromSeconds(Runner, IsRifle ? 0.1f : 0.24f);

        Vector3 eye = fpsCamera != null ? transform.TransformPoint(fpsCamera.transform.localPosition)
            : transform.position + Vector3.up * 1.6f;
        Vector3 direction = transform.rotation * Quaternion.Euler(Mathf.Clamp(aimPitch, -85f, 85f), 0f, 0f) * Vector3.forward;
        Physics.SyncTransforms();
        RaycastHit[] hits = Physics.RaycastAll(eye, direction, 80f, ~0, QueryTriggerInteraction.Collide);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        RailRouteLoop route = RailRouteLoop.Instance;
        foreach (RaycastHit hit in hits)
        {
            if (hit.transform.IsChildOf(transform)) continue;
            if (route != null && route.TryHitZombie(hit.collider, IsRifle ? 25 : 15, out bool weakPoint))
            {
                if (weakPoint) WeakPointHits++;
                return;
            }
            if (!hit.collider.isTrigger) return;
        }
    }

    private void RenderCombat()
    {
        RailRouteLoop route = RailRouteLoop.Instance;
        if (weaponVisual == null && CombatSlot != 0 && route != null)
        {
            bool firstPerson = IsLocalPlayer && fpsCamera != null;
            Transform hand = anim != null && anim.isHuman ? anim.GetBoneTransform(HumanBodyBones.RightHand) : null;
            Transform mount = firstPerson ? fpsCamera.transform : hand != null ? hand : transform;
            GameObject prefab = route.GetPersonalWeapon(IsRifle);
            if (prefab != null)
            {
                weaponVisual = Instantiate(prefab, mount, false);
                route.ApplyCombatMaterials(weaponVisual);
                weaponVisual.transform.localPosition = firstPerson
                    ? new Vector3(0.23f, -0.24f, IsRifle ? 0.59f : 0.47f)
                    : Vector3.zero;
                if (firstPerson) weaponVisual.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            }
        }
        bool show = CanFight && !FlashlightSelected && (!IsLocalPlayer || !isUsingTerminal);
        if (weaponVisual != null)
        {
            weaponVisual.SetActive(show);
            if (show && (!IsLocalPlayer || fpsCamera == null))
                weaponVisual.transform.rotation = transform.rotation * Quaternion.Euler(CameraPitch, 0f, 0f) *
                    Quaternion.Euler(0f, 180f, 0f);
        }
        if (!IsLocalPlayer || shownWeakPointHits == WeakPointHits) return;
        shownWeakPointHits = WeakPointHits;
        weakPointFlashUntil = Time.unscaledTime + 0.3f;
    }

    private void OnGUI()
    {
        if (GamePauseMenu.BlocksGameplayInput || Object == null || !Object.IsValid || !IsLocalPlayer || CombatSlot == 0) return;
        GUI.Label(new Rect(18f, 14f, 190f, 22f), $"HP  {Health}/{MaxHealth}");
        DrawCombatBar(37f, Health / (float)MaxHealth, new Color(0.85f, 0.22f, 0.23f));
        GUI.Label(new Rect(18f, 50f, 190f, 22f), $"STAMINA  {Mathf.CeilToInt(Stamina)}/{MaxStamina:F0}");
        DrawCombatBar(73f, Stamina / MaxStamina, new Color(0.27f, 0.76f, 0.55f));
        if (IsDowned)
        {
            GUI.Label(new Rect(Screen.width * 0.5f - 105f, Screen.height * 0.5f + 35f, 220f, 24f), "다운 상태 · 팀원 구조 대기");
            return;
        }
        if (!CanFight || isUsingTerminal) return;
        if (FindReviveTarget() != null)
            GUI.Label(new Rect(Screen.width * 0.5f - 65f, Screen.height * 0.5f + 35f, 180f, 24f), "[F] 팀원 구조");
        if (!FlashlightSelected)
            GUI.Label(new Rect(18f, 86f, 210f, 24f), $"{(IsRifle ? "M4" : "M1911")}   {Magazine} / {InventoryAmmunition}" +
                (ReloadTimer.IsRunning ? "   재장전 중" : ""));
        DrawCombatCrosshair();
    }

    private static void DrawCombatBar(float y, float fraction, Color color)
    {
        Color previous = GUI.color;
        GUI.color = new Color(0.1f, 0.1f, 0.1f, 0.8f);
        GUI.DrawTexture(new Rect(18f, y, 150f, 6f), Texture2D.whiteTexture);
        GUI.color = color;
        GUI.DrawTexture(new Rect(18f, y, 150f * fraction, 6f), Texture2D.whiteTexture);
        GUI.color = previous;
    }

    private void DrawCombatCrosshair()
    {
        Vector2 center = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        bool flash = !FlashlightSelected && Time.unscaledTime < weakPointFlashUntil;
        float pulse = flash ? (weakPointFlashUntil - Time.unscaledTime) / 0.3f : 0f;
        float size = 14f + pulse * 12f;
        Color color = GUI.color;
        Matrix4x4 matrix = GUI.matrix;
        if (flash) GUIUtility.RotateAroundPivot(45f, center);
        if (flash)
        {
            GUI.color = new Color(1f, 0.62f, 0.1f, pulse * 0.65f);
            DrawCross(center, size + 5f, 6f);
        }
        GUI.color = flash ? new Color(1f, 0.86f, 0.3f) : Color.white;
        DrawCross(center, size, 2f);
        GUI.matrix = matrix;
        GUI.color = color;
    }

    private static void DrawCross(Vector2 center, float size, float width)
    {
        GUI.DrawTexture(new Rect(center.x - size * 0.5f, center.y - width * 0.5f, size, width), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(center.x - width * 0.5f, center.y - size * 0.5f, width, size), Texture2D.whiteTexture);
    }
}

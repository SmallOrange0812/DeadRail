using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Animations.Rigging;
using UnityEngine.UI;
using Fusion;

public class EquipmentPivotController : NetworkBehaviour
{
    public enum EquipmentMode { None = 0, P2_Searchlight = 1, P3_HarpoonMount = 2 }

    [Header("현재 잡고 있는 장비 모드")]
    [Networked] public EquipmentMode CurrentMode { get; set; } = EquipmentMode.None;
    [Networked] private float HarpoonRestYaw { get; set; }

    [Header("탐조등 모드 시점 전환")]
    public bool isSearchlightFirstPerson = false;
    [Networked] public NetworkBool IsSearchlightOn { get; set; }

    private PlayerMovement _playerMovement;
    private HarpoonFarming _harpoonFarming;
    private Rig _bodyRig;
    private bool _usingHeavyWalk;
    private float _normalAnimationSpeed;
    private Animator _animator;

    [Header("UI 조준점 (직접 연결 - 비워두면 검은 박스 안 뜸!)")]
    public Image crosshairImage;

    [Header("P2 탐조등 피벗 & 손잡이 (자동 검색)")]
    public Transform searchlightYawPivot;
    public Transform searchlightPitchPivot;
    public Transform searchlightControlDesk;
    public Light searchlightLamp;

    [Header("기차 지붕 사다리 (자동 검색)")]
    public Transform roofAccessLadder;
    private Transform _alternateRoofAccessLadder;

    [Header("거치형 작살/무기 피벗 & 손잡이 (자동 검색)")]
    public Transform weaponRootYaw;
    public Transform weaponAimPivot;
    public Transform weaponBarrel;
    public Transform weaponGrip;
    public Transform weaponStock;
    public Transform weaponMuzzle;

    [Header("내 캐릭터 IK 타겟 (자동 연결)")]
    public Transform leftHandTarget;
    public Transform rightHandTarget;
    public Transform lookTarget;

    [Header("거리 제한 (허공에 손 들기 방지)")]
    public float maxGrabDistance = 1.6f;

    [Header("장비 상호작용 거리")]
    public float interactDistance = 2.5f;
    [SerializeField] private float ladderInteractDistance = 1.5f;
    [SerializeField] private float harpoonInteractDistance = 1.5f;
    private enum InteractionTarget { None, Ladder, Searchlight, Harpoon }

    [Header("회전축 및 총구 방향 보정")]
    public Vector3 yawAxis = new Vector3(0f, 0f, 1f);
    public Vector3 pitchAxis = new Vector3(1f, 0f, 0f);
    public float weaponYawOffset = -90f;
    public bool invertYaw = false;
    public bool invertPitch = false;

    [Header("양손 파지 위치 및 손목 각도 미세 보정")]
    public float handSpread = 0.14f;
    public Vector3 gripPositionOffset = new Vector3(0f, 0f, 0f);
    public Vector3 leftHandRotOffset = Vector3.zero;
    public Vector3 rightHandRotOffset = Vector3.zero;

    private Quaternion _initSearchlightYaw;
    private Quaternion _initSearchlightPitch;
    private Vector3 _searchlightForwardInYaw;
    private Quaternion _initWeaponYaw;
    private Quaternion _initWeaponPitch;
    private bool _initialized = false;
    private EquipmentMode _previousMode = EquipmentMode.None;
    private bool _isCrankPoseActive;
    private Transform _crankHandle;
    private Vector3 _crankLeftGripLocalPosition;
    private Vector3 _crankGripLocalPosition;
    private Quaternion _crankLeftHandRotationOffset = Quaternion.identity;
    private Quaternion _crankHandRotationOffset = Quaternion.identity;

    [ContextMenu("⚡ 뼈대 & IK 타겟 1초 자동 연결")]
    public void AutoSetupRigAndBones()
    {
        Transform[] allChildren = GetComponentsInChildren<Transform>(true);
        Transform lArm = null, lForeArm = null, lHand = null;
        Transform rArm = null, rForeArm = null, rHand = null;
        Transform spine = null;

        foreach (Transform t in allChildren)
        {
            if (t.name == "LeftHandTarget") leftHandTarget = t;
            else if (t.name == "RightHandTarget") rightHandTarget = t;
            else if (t.name == "LookTarget") lookTarget = t;
            else if (t.name.EndsWith("LeftArm")) lArm = t;
            else if (t.name.EndsWith("LeftForeArm")) lForeArm = t;
            else if (t.name.EndsWith("LeftHand")) lHand = t;
            else if (t.name.EndsWith("RightArm")) rArm = t;
            else if (t.name.EndsWith("RightForeArm")) rForeArm = t;
            else if (t.name.EndsWith("RightHand")) rHand = t;
            else if (t.name.EndsWith("Spine1") || (spine == null && t.name.EndsWith("Spine"))) spine = t;
        }

        TwoBoneIKConstraint[] twoBoneIKs = GetComponentsInChildren<TwoBoneIKConstraint>(true);
        foreach (var ik in twoBoneIKs)
        {
            if (ik.name.Contains("Left"))
            {
                ik.data.root = lArm; ik.data.mid = lForeArm; ik.data.tip = lHand;
                ik.data.target = leftHandTarget;
                ik.data.targetPositionWeight = 1f; ik.data.targetRotationWeight = 1f;
            }
            else if (ik.name.Contains("Right"))
            {
                ik.data.root = rArm; ik.data.mid = rForeArm; ik.data.tip = rHand;
                ik.data.target = rightHandTarget;
                ik.data.targetPositionWeight = 1f; ik.data.targetRotationWeight = 1f;
            }
        }

        MultiAimConstraint aimIK = GetComponentInChildren<MultiAimConstraint>(true);
        if (aimIK != null && spine != null && lookTarget != null)
        {
            aimIK.data.constrainedObject = spine;
            var sources = new WeightedTransformArray(0);
            sources.Add(new WeightedTransform(lookTarget, 1f));
            aimIK.data.sourceObjects = sources;
            aimIK.data.aimAxis = MultiAimConstraintData.Axis.Z;
            aimIK.data.limits = new Vector2(-90f, 90f);
        }
    }

    public override void Spawned()
    {
        if (HasStateAuthority)
        {
            CurrentMode = EquipmentMode.None;
            IsSearchlightOn = false;
        }
        isSearchlightFirstPerson = false;

        _playerMovement = GetComponent<PlayerMovement>();
        _harpoonFarming = GetComponent<HarpoonFarming>();
        _bodyRig = GetComponentInChildren<Rig>(true);
        _animator = GetComponentInChildren<Animator>(true);
        _previousMode = CurrentMode;

        // Bone and constraint references are authored in Player_A.prefab.
        // RigBuilder builds its graph from OnEnable; rebuilding here during
        // Fusion's Spawned callback can invalidate TwoBoneIK stream handles.

        FindTrainEquipment();
        ApplyModeSettings();
    }

    private void FindTrainEquipment()
    {
        Transform[] allTransforms = FindObjectsByType<Transform>(FindObjectsSortMode.None);
        foreach (Transform t in allTransforms)
        {
            if (searchlightYawPivot == null && t.name.Contains("Searchlight_YawPivot")) searchlightYawPivot = t;
            else if (searchlightPitchPivot == null && t.name.Contains("Searchlight_PitchPivot")) searchlightPitchPivot = t;
            else if (searchlightControlDesk == null && t.name.Contains("P2_Searchlight_Control")) searchlightControlDesk = t;
            else if (weaponRootYaw == null && t.name.Contains("P3_Weapon_Mount_ROOT")) weaponRootYaw = t;
            else if (weaponAimPivot == null && t.name.Contains("P3_AimPivot")) weaponAimPivot = t;
            else if (t.name.Contains("Roof_Access_Ladder"))
            {
                if (roofAccessLadder == null) roofAccessLadder = t;
                else if (_alternateRoofAccessLadder == null && t != roofAccessLadder)
                    _alternateRoofAccessLadder = t;
            }
        }

        // Resolve weapon parts only inside this mounted weapon. The old global
        // name search could bind a barrel/grip from another scene object and
        // produce a sideways direction for the hand IK.
        if (weaponAimPivot != null)
        {
            foreach (Transform child in weaponAimPivot.GetComponentsInChildren<Transform>(true))
            {
                if (weaponBarrel == null && child.name == "Weapon_Barrel")
                    weaponBarrel = child;
                if (weaponGrip == null && child.name == "Weapon_Grip")
                    weaponGrip = child;
                if (weaponStock == null && child.name == "Weapon_Stock")
                    weaponStock = child;
            }
        }

        if (searchlightYawPivot != null) _initSearchlightYaw = searchlightYawPivot.localRotation;
        if (searchlightPitchPivot != null)
        {
            _initSearchlightPitch = searchlightPitchPivot.localRotation;
            if (searchlightYawPivot != null)
                _searchlightForwardInYaw = searchlightYawPivot.InverseTransformDirection(searchlightPitchPivot.up);
            searchlightLamp = searchlightPitchPivot.GetComponentInChildren<Light>(true);

            if (searchlightLamp == null)
            {
                GameObject lightObj = new GameObject("Searchlight_SpotLight");
                lightObj.transform.SetParent(searchlightPitchPivot, false);
                lightObj.transform.localPosition = Vector3.zero;
                lightObj.transform.localEulerAngles = new Vector3(-90f, 0f, 0f);

                searchlightLamp = lightObj.AddComponent<Light>();
                searchlightLamp.type = LightType.Spot;
                searchlightLamp.range = 150f;
                searchlightLamp.spotAngle = 45f;
                searchlightLamp.intensity = 500f;
                searchlightLamp.enabled = false;
            }
        }

        if (weaponRootYaw != null) _initWeaponYaw = weaponRootYaw.localRotation;
        if (weaponAimPivot != null) _initWeaponPitch = weaponAimPivot.localRotation;

        if (_harpoonFarming != null)
        {
            weaponMuzzle = FindOrCreateWeaponMuzzle();
            _harpoonFarming.harpoonMuzzle = weaponMuzzle;
        }

        _initialized = true;
    }

    private Transform FindOrCreateWeaponMuzzle()
    {
        if (weaponMuzzle != null)
            return weaponMuzzle;
        if (weaponAimPivot == null)
            return null;

        // Prefer an authored point at the circular barrel opening, if the model
        // already contains one. Search only the train weapon hierarchy, never a
        // player's unrelated FirePoint.
        foreach (Transform child in weaponAimPivot.GetComponentsInChildren<Transform>(true))
        {
            string lowerName = child.name.ToLowerInvariant();
            if (lowerName.Contains("muzzle") || lowerName.Contains("barreltip") ||
                lowerName.Contains("barrelend") || lowerName == "firepoint")
            {
                return child;
            }
        }

        Transform generated = weaponAimPivot.Find("Runtime_HarpoonMuzzle");
        if (generated == null)
        {
            GameObject marker = new GameObject("Runtime_HarpoonMuzzle");
            generated = marker.transform;
            generated.SetParent(weaponAimPivot, false);
        }

        if (weaponStock != null && weaponBarrel != null)
        {
            Vector3 barrelCenter = GetVisualCenter(weaponBarrel);
            Vector3 forward = (barrelCenter - GetVisualCenter(weaponStock)).normalized;
            if (forward.sqrMagnitude > 0f)
            {
                generated.position = barrelCenter + forward * GetProjectedVisualExtent(weaponBarrel, forward);
                return generated;
            }
        }

        if (!TryGetWeaponLocalBounds(out Bounds localBounds))
            return generated;

        // The barrel is the weapon assembly's longest dimension. Put the point
        // at that forward end, where the image shows the round opening.
        Vector3 extents = localBounds.extents;
        int longestAxis = extents.x >= extents.y && extents.x >= extents.z ? 0
            : extents.y >= extents.z ? 1 : 2;
        Vector3 localMuzzle = localBounds.center;
        if (longestAxis == 0) localMuzzle.x += extents.x;
        else if (longestAxis == 1) localMuzzle.y += extents.y;
        else localMuzzle.z += extents.z;
        generated.localPosition = localMuzzle;
        return generated;
    }

    private bool TryGetWeaponLocalBounds(out Bounds localBounds)
    {
        localBounds = default;
        if (weaponAimPivot == null)
            return false;

        bool hasBounds = false;
        foreach (Renderer item in weaponAimPivot.GetComponentsInChildren<Renderer>(true))
        {
            Bounds worldBounds = item.bounds;
            for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
            for (int z = -1; z <= 1; z += 2)
            {
                Vector3 corner = worldBounds.center + Vector3.Scale(
                    worldBounds.extents, new Vector3(x, y, z));
                Vector3 localCorner = weaponAimPivot.InverseTransformPoint(corner);
                if (!hasBounds)
                {
                    localBounds = new Bounds(localCorner, Vector3.zero);
                    hasBounds = true;
                }
                else
                {
                    localBounds.Encapsulate(localCorner);
                }
            }
        }

        return hasBounds;
    }

    private void ApplyModeSettings()
    {
        if (_playerMovement == null || !_playerMovement.IsLocalPlayer) return;

        isSearchlightFirstPerson = CurrentMode == EquipmentMode.P2_Searchlight;
        _playerMovement.CameraOverrideTarget = isSearchlightFirstPerson ? searchlightPitchPivot : null;
        bool isHoldingHarpoon = CurrentMode == EquipmentMode.P3_HarpoonMount;

        if (crosshairImage != null)
        {
            crosshairImage.enabled = isHoldingHarpoon && !GamePauseMenu.BlocksGameplayInput;
            if (isHoldingHarpoon) crosshairImage.color = Color.cyan;
        }
    }

    private void Update()
    {
        if (Object == null || !Object.IsValid || _playerMovement == null || !_playerMovement.IsLocalPlayer) return;
        if (GamePauseMenu.BlocksGameplayInput || _playerMovement.IsDowned || _playerMovement.IsClimbingLadder || CurrentMode != EquipmentMode.P2_Searchlight) return;
        if ((TerminalUIManager.Instance != null && TerminalUIManager.Instance.IsUiOpen) ||
            (RailRouteLoop.Instance != null && RailRouteLoop.Instance.Ready && RailRouteLoop.Instance.IsTransition)) return;

        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            Rpc_ToggleSearchlight();
    }

    public override void FixedUpdateNetwork()
    {
        if (HasStateAuthority && IsSearchlightOn && TreadmillManager.Instance != null && !TreadmillManager.Instance.HasPower)
            IsSearchlightOn = false;
    }

    [Rpc(RpcSources.InputAuthority | RpcSources.StateAuthority, RpcTargets.StateAuthority)]
    private void Rpc_ToggleSearchlight()
    {
        if (CurrentMode != EquipmentMode.P2_Searchlight || _playerMovement == null || _playerMovement.IsDowned || !_playerMovement.HasRole(TrainRole.Searchlight)) return;
        if (TreadmillManager.Instance != null && !TreadmillManager.Instance.HasPower) return;
        IsSearchlightOn = !IsSearchlightOn;
    }

    private bool IsNearSearchlightControl()
    {
        Transform station = searchlightControlDesk != null ? searchlightControlDesk : searchlightPitchPivot;
        return station != null && Vector3.Distance(transform.position, station.position) <= interactDistance;
    }

    public float GetNearestInteractionDistance()
    {
        GetNearestInteractionTarget(out float distance);
        return distance;
    }

    private InteractionTarget GetNearestInteractionTarget(out float nearestDistance)
    {
        if (CurrentMode == EquipmentMode.P2_Searchlight)
        {
            nearestDistance = 0f;
            return InteractionTarget.Searchlight;
        }

        if (CurrentMode == EquipmentMode.P3_HarpoonMount)
        {
            nearestDistance = 0f;
            return InteractionTarget.Harpoon;
        }

        if (_playerMovement != null && _playerMovement.IsClimbingLadder)
        {
            nearestDistance = 0f;
            return InteractionTarget.Ladder;
        }

        nearestDistance = float.PositiveInfinity;
        InteractionTarget target = InteractionTarget.None;

        Transform ladder = GetNearestLadder(out Bounds ladderBounds);
        bool nearWeapon = IsNearWeaponMount();
        if ((ladder == null && !nearWeapon) ||
            !ConsoleInteractable.TryGetAimedHit(_playerMovement, out RaycastHit hit))
            return target;

        if (ladder != null &&
            hit.collider.transform.parent == ladder.parent &&
            ConsoleInteractable.IsLadderPart(hit.collider.name))
        {
            float distance = Vector3.Distance(transform.position, ladderBounds.ClosestPoint(transform.position));
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                target = InteractionTarget.Ladder;
            }
        }

        Transform mount = weaponRootYaw != null ? weaponRootYaw : weaponAimPivot;
        if (nearWeapon && _playerMovement.HasRole(TrainRole.Gunner) && mount != null &&
            (hit.collider.transform == mount || hit.collider.transform.IsChildOf(mount)))
        {
            float distance = GetWeaponMountDistance();
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                target = InteractionTarget.Harpoon;
            }
        }

        return target;
    }

    private bool IsEquipmentInteractionTarget(InteractionTarget target)
    {
        return RailRouteLoop.CanReachTrainInterior(transform.position) &&
               GetNearestInteractionTarget(out _) == target &&
               !ConsoleInteractable.IsConsoleNearestInteraction(_playerMovement);
    }

    private Transform GetNearestLadder(out Bounds ladderBounds)
    {
        Transform nearest = null;
        float nearestDistance = float.MaxValue;
        ladderBounds = default;

        Transform[] ladders = { roofAccessLadder, _alternateRoofAccessLadder };
        foreach (Transform ladder in ladders)
        {
            if (ladder == null) continue;

            Renderer[] renderers = ladder.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) continue;

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);

            float distance = Vector3.Distance(transform.position, bounds.ClosestPoint(transform.position));
            if (distance < nearestDistance)
            {
                nearest = ladder;
                nearestDistance = distance;
                ladderBounds = bounds;
            }
        }

        return nearestDistance <= ladderInteractDistance ? nearest : null;
    }

    public bool TryBeginLadderClimb()
    {
        if (!IsEquipmentInteractionTarget(InteractionTarget.Ladder)) return false;

        Transform ladder = GetNearestLadder(out Bounds bounds);
        if (ladder == null) return false;

        Bounds rungBounds = default;
        bool hasRungs = false;
        if (ladder.parent != null)
        {
            foreach (Transform part in ladder.parent)
            {
                if (part.name != "Ladder_Rung" && !part.name.StartsWith("Ladder_Rung.")) continue;
                if (!TryGetRendererBounds(part, out Bounds rung)) continue;
                if (hasRungs) rungBounds.Encapsulate(rung);
                else rungBounds = rung;
                hasRungs = true;
            }
        }
        Vector3 climbCenter = hasRungs ? rungBounds.center : bounds.center;

        Transform trainRoot = ladder;
        while (trainRoot.parent != null && trainRoot.name != "Train")
            trainRoot = trainRoot.parent;

        Vector3 trainCenter = trainRoot.position;
        Renderer[] trainRenderers = trainRoot.GetComponentsInChildren<Renderer>(true);
        if (trainRenderers.Length > 0)
        {
            Bounds trainBounds = trainRenderers[0].bounds;
            for (int i = 1; i < trainRenderers.Length; i++)
                trainBounds.Encapsulate(trainRenderers[i].bounds);
            trainCenter = trainBounds.center;
        }

        Vector3 inward = trainCenter - climbCenter;
        inward.y = 0f;
        if (roofAccessLadder != null && _alternateRoofAccessLadder != null &&
            roofAccessLadder.parent == ladder.parent && _alternateRoofAccessLadder.parent == ladder.parent)
        {
            Vector3 across = Vector3.ProjectOnPlane(_alternateRoofAccessLadder.position - roofAccessLadder.position, Vector3.up);
            if (across.sqrMagnitude > 0.001f)
                inward = Vector3.ProjectOnPlane(inward, across.normalized);
        }
        if (inward.sqrMagnitude > 0.001f) inward.Normalize();

        Vector3 ladderCenterLocal = trainRoot.InverseTransformPoint(climbCenter);
        Vector3 inwardLocal = trainRoot.InverseTransformVector(inward);
        inwardLocal.y = 0f;
        if (inwardLocal.sqrMagnitude > 0.001f) inwardLocal.Normalize();

        Vector3 ladderBottomLocal = trainRoot.InverseTransformPoint(
            new Vector3(bounds.center.x, bounds.min.y, bounds.center.z));
        Vector3 ladderTopLocal = trainRoot.InverseTransformPoint(
            new Vector3(bounds.center.x, bounds.max.y, bounds.center.z));
        return _playerMovement.BeginLadderClimb(
            trainRoot, ladderCenterLocal, inwardLocal, ladderBottomLocal.y, ladderTopLocal.y);
    }

    public bool TryInteractWithEquipment()
    {
        if (!HasStateAuthority) return false;
        if (CurrentMode == EquipmentMode.P2_Searchlight)
            return TryToggleMode(EquipmentMode.P2_Searchlight);
        if (CurrentMode == EquipmentMode.P3_HarpoonMount)
            return TryToggleMode(EquipmentMode.P3_HarpoonMount);
        if (IsEquipmentInteractionTarget(InteractionTarget.Harpoon))
            return TryToggleMode(EquipmentMode.P3_HarpoonMount);
        return false;
    }

    private float GetWeaponMountDistance()
    {
        Transform station = weaponRootYaw != null ? weaponRootYaw : weaponGrip != null ? weaponGrip : weaponAimPivot;
        if (station == null) return float.PositiveInfinity;

        if (!TryGetRendererBounds(station, out Bounds bounds))
            return Vector3.Distance(transform.position, station.position);

        return Vector3.Distance(transform.position, bounds.ClosestPoint(transform.position));
    }

    private bool IsNearWeaponMount()
    {
        if (!IsAtRoofLadderHeight())
            return false;

        // Measure to the visible mount rather than its pivot, so the player can
        // interact from the ladder landing without stepping over the yellow rail.
        return GetWeaponMountDistance() <= harpoonInteractDistance;
    }

    private bool IsAtRoofLadderHeight()
    {
        Transform[] ladders = { roofAccessLadder, _alternateRoofAccessLadder };
        float nearestDistance = float.MaxValue;
        Bounds nearestBounds = default;
        bool found = false;

        foreach (Transform ladder in ladders)
        {
            if (ladder == null || !TryGetRendererBounds(ladder, out Bounds bounds))
                continue;

            Vector3 closest = bounds.ClosestPoint(transform.position);
            float dx = transform.position.x - closest.x;
            float dz = transform.position.z - closest.z;
            float distance = dx * dx + dz * dz;
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearestBounds = bounds;
                found = true;
            }
        }

        // The player root is near the roof deck when it reaches the ladder's
        // upper end. Comparing against the middle made the lower ladder trigger
        // harpoon interaction too early.
        return found && transform.position.y >= nearestBounds.max.y - 0.75f;
    }

    public bool TryInteractWithHarpoonMount()
    {
        // Only let the harpoon consume E from the roof, while close to it.
        if (!HasStateAuthority || !IsAtRoofLadderHeight() || !IsNearWeaponMount() ||
            !IsEquipmentInteractionTarget(InteractionTarget.Harpoon))
            return false;

        return TryToggleMode(EquipmentMode.P3_HarpoonMount);
    }

    private static bool TryGetRendererBounds(Transform root, out Bounds bounds)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            bounds = default;
            return false;
        }

        bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);
        return true;
    }

    public bool TryToggleMode(EquipmentMode mode)
    {
        if (!HasStateAuthority || mode == EquipmentMode.None)
            return false;

        if (CurrentMode != mode)
        {
            TrainRole required = mode == EquipmentMode.P2_Searchlight ? TrainRole.Searchlight : TrainRole.Gunner;
            if (_playerMovement == null || _playerMovement.IsDowned || !_playerMovement.HasRole(required)) return false;
            if (mode == EquipmentMode.P2_Searchlight && !IsNearSearchlightControl()) return false;
            if (mode == EquipmentMode.P3_HarpoonMount && (!IsNearWeaponMount() || !TryAlignHarpoonView())) return false;
        }

        CurrentMode = CurrentMode == mode ? EquipmentMode.None : mode;
        if (CurrentMode != EquipmentMode.P2_Searchlight)
            IsSearchlightOn = false;
        ApplyModeSettings();
        return true;
    }

    [Rpc(RpcSources.InputAuthority | RpcSources.StateAuthority, RpcTargets.StateAuthority)]
    internal void Rpc_LeaveStation(EquipmentMode mode)
    {
        if (mode != EquipmentMode.None && CurrentMode == mode) TryToggleMode(mode);
    }

    private bool TryAlignHarpoonView()
    {
        Transform stock = weaponStock != null ? weaponStock : weaponGrip;
        Transform barrel = weaponBarrel != null ? weaponBarrel : weaponMuzzle;
        if (stock == null || barrel == null || _playerMovement == null)
            return false;

        if (weaponRootYaw != null) weaponRootYaw.localRotation = _initWeaponYaw;
        if (weaponAimPivot != null) weaponAimPivot.localRotation = _initWeaponPitch;
        Vector3 barrelForward = Vector3.ProjectOnPlane(GetVisualCenter(barrel) - GetVisualCenter(stock), Vector3.up);
        if (barrelForward.sqrMagnitude < 0.001f)
            return false;

        Vector3 trainForward = searchlightYawPivot != null
            ? Vector3.ProjectOnPlane(searchlightYawPivot.position - stock.position, Vector3.up)
            : Vector3.forward;
        if (trainForward.sqrMagnitude < 0.001f)
            trainForward = Vector3.forward;

        HarpoonRestYaw = Quaternion.LookRotation(barrelForward, Vector3.up).eulerAngles.y;
        transform.rotation = Quaternion.LookRotation(trainForward, Vector3.up);
        _playerMovement.CameraPitch = 0f;
        return true;
    }

    internal string GetEquipmentPrompt(out Transform outlineTarget)
    {
        outlineTarget = null;
        if (_playerMovement == null || !_playerMovement.IsLocalPlayer) return null;
        if (_playerMovement.IsClimbingLadder) return "[W] 오르기  [S] 내려가기  [F] 사다리 놓기";
        if (CurrentMode == EquipmentMode.P2_Searchlight && IsNearSearchlightControl())
            return $"[F] 나가기  [좌클릭] 탐조등 {(IsSearchlightOn ? "끄기" : "켜기")}";
        if (CurrentMode == EquipmentMode.P3_HarpoonMount) return "[F] 작살에서 나가기";
        if (CurrentMode != EquipmentMode.None) return null;

        switch (GetNearestInteractionTarget(out _))
        {
            case InteractionTarget.Ladder:
                outlineTarget = GetNearestLadder(out _);
                if (outlineTarget != null && roofAccessLadder != null &&
                    outlineTarget.parent == roofAccessLadder.parent)
                    outlineTarget = roofAccessLadder;
                return "[F]  사다리 잡기";
            case InteractionTarget.Harpoon:
                outlineTarget = weaponRootYaw != null ? weaponRootYaw : weaponAimPivot;
                return "[F]  P3 거치형 작살 사용";
            default:
                return null;
        }
    }

    private void LateUpdate()
    {
        if (!_initialized || _playerMovement == null) return;

        if (_previousMode == EquipmentMode.P2_Searchlight && CurrentMode != EquipmentMode.P2_Searchlight && searchlightLamp != null)
            searchlightLamp.enabled = false;
        _previousMode = CurrentMode;

        ApplyModeSettings();

        if (_isCrankPoseActive)
        {
            if (_bodyRig != null) _bodyRig.weight = 1f;
            if (_animator != null && _animator.layerCount > 1)
                _animator.SetLayerWeight(1, 0f);
            if (lookTarget != null && _crankHandle != null)
                lookTarget.position = _crankHandle.position;
            UpdateCrankHandTarget();
            return;
        }

        bool heavyWalk = CurrentMode == EquipmentMode.None && _playerMovement.IsCarryingHeavyBattery;
        if (_animator != null && heavyWalk != _usingHeavyWalk)
        {
            if (heavyWalk) _normalAnimationSpeed = _animator.speed;
            _animator.speed = heavyWalk ? _normalAnimationSpeed * 0.65f : _normalAnimationSpeed;
            _usingHeavyWalk = heavyWalk;
        }

        if (heavyWalk)
        {
            if (_bodyRig != null) _bodyRig.weight = 1f;
            if (_animator != null && _animator.layerCount > 1) _animator.SetLayerWeight(1, 0f);
            Vector3 center = RailRouteLoop.GetBatteryCarryPosition(transform);
            Vector3 grip = center - transform.forward * 0.12f - Vector3.up * 0.10f;
            Vector3 leftGrip = grip - transform.right * 0.20f;
            Vector3 rightGrip = grip + transform.right * 0.20f;
            PlayerRef carrier = Runner.GameMode == GameMode.Shared ? Object.StateAuthority : Object.InputAuthority;
            if (RailRouteLoop.Instance.TryGetBatteryHandleGrips(carrier, transform, out Vector3 handleLeft, out Vector3 handleRight))
            {
                leftGrip = handleLeft;
                rightGrip = handleRight;
            }
            if (leftHandTarget != null)
                leftHandTarget.SetPositionAndRotation(leftGrip,
                    transform.rotation * Quaternion.Euler(leftHandRotOffset));
            if (rightHandTarget != null)
                rightHandTarget.SetPositionAndRotation(rightGrip,
                    transform.rotation * Quaternion.Euler(rightHandRotOffset));
            if (lookTarget != null) lookTarget.position = transform.position + transform.forward * 5f + Vector3.up * 1.2f;
            return;
        }

        if (CurrentMode == EquipmentMode.None)
        {
            if (_bodyRig != null) _bodyRig.weight = 0f;

            if (_animator != null && _animator.layerCount > 1)
                _animator.SetLayerWeight(1, 0f);

            return;
        }
        else
        {
            if (_animator != null && _animator.layerCount > 1)
                _animator.SetLayerWeight(1, 1f);
        }

        if (CurrentMode == EquipmentMode.P2_Searchlight)
        {
            AimSearchlight(transform.eulerAngles.y, _playerMovement.CameraPitch);

            bool trainHasPower = TreadmillManager.Instance == null || TreadmillManager.Instance.HasPower;
            if (searchlightLamp != null)
                searchlightLamp.enabled = IsSearchlightOn && trainHasPower;

            Transform targetStation = searchlightControlDesk != null ? searchlightControlDesk : searchlightPitchPivot;
            if (targetStation != null)
            {
                // Entering this mode already requires proximity to the station.
                // Keep IK active while operating it; measuring from the player root
                // to a pivot can incorrectly disable the rig even while seated at it.
                if (_bodyRig != null) _bodyRig.weight = 1f;

                Vector3 deskTop = targetStation.position + Vector3.up * 0.9f + transform.TransformDirection(gripPositionOffset);
                if (leftHandTarget != null)
                    leftHandTarget.SetPositionAndRotation(deskTop - transform.right * handSpread, transform.rotation * Quaternion.Euler(leftHandRotOffset));
                if (rightHandTarget != null)
                    rightHandTarget.SetPositionAndRotation(deskTop + transform.right * handSpread, transform.rotation * Quaternion.Euler(rightHandRotOffset));
            }
        }
        else if (CurrentMode == EquipmentMode.P3_HarpoonMount)
        {
            float finalWeaponYaw = Mathf.DeltaAngle(HarpoonRestYaw, transform.eulerAngles.y);
            if (weaponRootYaw != null)
            {
                Vector3 upInParent = weaponRootYaw.parent != null
                    ? weaponRootYaw.parent.InverseTransformDirection(Vector3.up)
                    : Vector3.up;
                weaponRootYaw.localRotation = Quaternion.AngleAxis(finalWeaponYaw, upInParent) * _initWeaponYaw;
            }

            if (weaponAimPivot != null)
            {
                Vector3 rightInParent = weaponAimPivot.parent != null
                    ? weaponAimPivot.parent.InverseTransformDirection(transform.right)
                    : transform.right;
                weaponAimPivot.localRotation =
                    Quaternion.AngleAxis(_playerMovement.CameraPitch, rightInParent) * _initWeaponPitch;
            }

            // In the current train model Weapon_Grip is the round side pivot at
            // the mount, not the point where a player should hold the launcher.
            // Weapon_Stock is the rear block behind the receiver, so use it as
            // the trigger-hand anchor and only fall back to Weapon_Grip when a
            // model has no stock transform.
            Transform rearHandAnchor = weaponStock != null ? weaponStock : weaponGrip;
            if (rearHandAnchor != null)
            {
                // Harpoon mode can only be entered from the roof interaction range.
                // The player root is offset from the stock while operating the mount,
                // so a second root-to-stock distance gate turns IK off mid-use.
                if (_bodyRig != null) _bodyRig.weight = 1f;

                Vector3 stockCenter = GetVisualCenter(rearHandAnchor);
                Vector3 barrelDirection = weaponBarrel != null
                    ? GetVisualCenter(weaponBarrel) - stockCenter
                    : weaponMuzzle != null
                        ? weaponMuzzle.position - rearHandAnchor.position
                        : weaponAimPivot != null
                            ? weaponAimPivot.forward
                            : rearHandAnchor.forward;
                if (barrelDirection.sqrMagnitude < 0.001f)
                    barrelDirection = rearHandAnchor.forward;
                barrelDirection.Normalize();

                Quaternion gripRotation = Quaternion.LookRotation(barrelDirection, Vector3.up);
                Vector3 gripRight = gripRotation * Vector3.right;
                Vector3 gripUp = gripRotation * Vector3.up;
                float stockHalfLength = GetProjectedVisualExtent(rearHandAnchor, barrelDirection);
                Vector3 rearGrip = stockCenter - barrelDirection * (stockHalfLength + 0.04f)
                    - gripUp * 0.10f + gripRight * Mathf.Max(0.10f, handSpread)
                    + rearHandAnchor.TransformDirection(gripPositionOffset);
                Vector3 supportCenter = weaponBarrel != null
                    ? Vector3.Lerp(stockCenter, GetVisualCenter(weaponBarrel), 0.65f)
                    : stockCenter + barrelDirection * 0.28f;
                Vector3 supportGrip = supportCenter - gripUp * 0.13f
                    - gripRight * Mathf.Max(0.10f, handSpread);

                if (leftHandTarget != null)
                    leftHandTarget.SetPositionAndRotation(
                        supportGrip,
                        gripRotation * Quaternion.Euler(leftHandRotOffset));
                if (rightHandTarget != null)
                    rightHandTarget.SetPositionAndRotation(
                        rearGrip,
                        gripRotation * Quaternion.Euler(rightHandRotOffset));
            }
        }

        if (lookTarget != null)
            lookTarget.position = transform.position + transform.forward * 5f + Vector3.up * 1.2f;
    }

    private void AimSearchlight(float yaw, float pitch)
    {
        if (searchlightYawPivot == null || searchlightPitchPivot == null) return;

        Quaternion neutralYaw = searchlightYawPivot.parent.rotation * _initSearchlightYaw;
        Vector3 neutralForward = neutralYaw * _searchlightForwardInYaw;
        float neutralHeading = Mathf.Atan2(neutralForward.x, neutralForward.z) * Mathf.Rad2Deg;
        Quaternion heading = Quaternion.AngleAxis(yaw - neutralHeading, Vector3.up) * neutralYaw;
        searchlightYawPivot.rotation = heading;

        Vector3 pitchAxisInWorld = Quaternion.Euler(0f, yaw, 0f) * Vector3.right;
        searchlightPitchPivot.rotation = Quaternion.AngleAxis(pitch, pitchAxisInWorld) * heading * _initSearchlightPitch;
    }

    private static Vector3 GetVisualCenter(Transform root)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
            return root.position;

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);
        return bounds.center;
    }

    private static float GetProjectedVisualExtent(Transform root, Vector3 axis)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
            return 0f;

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);

        axis.Normalize();
        Vector3 e = bounds.extents;
        return Mathf.Abs(axis.x) * e.x + Mathf.Abs(axis.y) * e.y + Mathf.Abs(axis.z) * e.z;
    }

    public void BeginCrankPose(Transform handle, Vector3 gripWorldPosition)
    {
        if (handle == null || rightHandTarget == null)
            return;
        if (_isCrankPoseActive && _crankHandle == handle)
            return;

        _crankHandle = handle;
        _crankGripLocalPosition = handle.InverseTransformPoint(gripWorldPosition);
        // The crank is operated with both hands. Keep the left hand slightly
        // offset from the right along the player's shoulder line so both wrists
        // reach the handle instead of leaving the support arm in its idle pose.
        Vector3 leftGripWorldPosition = gripWorldPosition - transform.right * 0.18f;
        _crankLeftGripLocalPosition = handle.InverseTransformPoint(leftGripWorldPosition);
        Transform handBone = _animator != null ? _animator.GetBoneTransform(HumanBodyBones.RightHand) : null;
        Quaternion handRotation = handBone != null ? handBone.rotation : rightHandTarget.rotation;
        _crankHandRotationOffset = Quaternion.Inverse(handle.rotation) * handRotation;
        if (leftHandTarget != null)
        {
            Transform leftHandBone = _animator != null ? _animator.GetBoneTransform(HumanBodyBones.LeftHand) : null;
            Quaternion leftHandRotation = leftHandBone != null ? leftHandBone.rotation : leftHandTarget.rotation;
            _crankLeftHandRotationOffset = Quaternion.Inverse(handle.rotation) * leftHandRotation;
        }
        _isCrankPoseActive = true;
    }

    public void EndCrankPose()
    {
        _isCrankPoseActive = false;
        _crankHandle = null;
    }

    private void UpdateCrankHandTarget()
    {
        if (!_isCrankPoseActive || _crankHandle == null || rightHandTarget == null)
            return;

        rightHandTarget.SetPositionAndRotation(
            _crankHandle.TransformPoint(_crankGripLocalPosition),
            _crankHandle.rotation * _crankHandRotationOffset);

        if (leftHandTarget != null)
        {
            leftHandTarget.SetPositionAndRotation(
                _crankHandle.TransformPoint(_crankLeftGripLocalPosition),
                _crankHandle.rotation * _crankLeftHandRotationOffset);
        }
    }
}

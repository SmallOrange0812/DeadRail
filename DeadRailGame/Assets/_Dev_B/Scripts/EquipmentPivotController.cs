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

    [Header("탐조등 모드 시점 전환")]
    public bool isSearchlightFirstPerson = false;

    private PlayerMovement _playerMovement;
    private HarpoonFarming _harpoonFarming;
    private RaycastWeapon _raycastWeapon;
    private Rig _bodyRig;
    private Animator _animator;

    [Header("UI 조준점 (직접 연결 - 비워두면 검은 박스 안 뜸!)")]
    public Image crosshairImage;

    [Header("P2 탐조등 피벗 & 손잡이 (자동 검색)")]
    public Transform searchlightYawPivot;
    public Transform searchlightPitchPivot;
    public Transform searchlightControlDesk;
    public Light searchlightLamp;

    [Header("거치형 작살/무기 피벗 & 손잡이 (자동 검색)")]
    public Transform weaponRootYaw;
    public Transform weaponAimPivot;
    public Transform weaponGrip;

    [Header("내 캐릭터 IK 타겟 (자동 연결)")]
    public Transform leftHandTarget;
    public Transform rightHandTarget;
    public Transform lookTarget;

    [Header("거리 제한 (허공에 손 들기 방지)")]
    public float maxGrabDistance = 1.6f;

    [Header("회전축 및 총구 방향 보정")]
    public Vector3 yawAxis = new Vector3(0f, 0f, 1f);
    public Vector3 pitchAxis = new Vector3(1f, 0f, 0f);
    public float weaponYawOffset = -90f;
    public bool invertYaw = false;
    public bool invertPitch = false;

    [Header("양손 파지 위치 및 손목 각도 미세 보정")]
    public float handSpread = 0.14f;
    public Vector3 gripPositionOffset = new Vector3(0f, 0f, 0f);
    public Vector3 leftHandRotOffset = new Vector3(90f, 0f, 0f);
    public Vector3 rightHandRotOffset = new Vector3(90f, 0f, 0f);

    private Quaternion _initSearchlightYaw;
    private Quaternion _initSearchlightPitch;
    private Quaternion _initWeaponYaw;
    private Quaternion _initWeaponPitch;
    private bool _initialized = false;

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
        CurrentMode = EquipmentMode.None;
        isSearchlightFirstPerson = false;

        _playerMovement = GetComponent<PlayerMovement>();
        _harpoonFarming = GetComponent<HarpoonFarming>();
        _raycastWeapon = GetComponent<RaycastWeapon>();
        _bodyRig = GetComponentInChildren<Rig>(true);
        _animator = GetComponentInChildren<Animator>(true);

        // AutoSetupRigAndBones();  // ✅ 이 줄을 주석 처리(//) 하거나 삭제하세요!

        RigBuilder rigBuilder = GetComponent<RigBuilder>();
        if (rigBuilder != null) rigBuilder.Build();

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
            else if (searchlightControlDesk == null && t.name.Contains("_P2_Searchlight_Control")) searchlightControlDesk = t;
            else if (weaponRootYaw == null && t.name.Contains("P3_Weapon_Mount_ROOT")) weaponRootYaw = t;
            else if (weaponAimPivot == null && t.name.Contains("P3_AimPivot")) weaponAimPivot = t;
            else if (weaponGrip == null && t.name.Contains("Weapon_Grip")) weaponGrip = t;
        }

        if (searchlightYawPivot != null) _initSearchlightYaw = searchlightYawPivot.localRotation;
        if (searchlightPitchPivot != null)
        {
            _initSearchlightPitch = searchlightPitchPivot.localRotation;
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
            GameObject firePointObj = GameObject.Find("FirePoint");
            if (firePointObj != null)
                _harpoonFarming.harpoonMuzzle = firePointObj.transform;
            else
                _harpoonFarming.harpoonMuzzle = weaponAimPivot;
        }

        _initialized = true;
    }

    private void ApplyModeSettings()
    {
        if (!HasStateAuthority) return;

        if (_playerMovement != null)
        {
            if (CurrentMode == EquipmentMode.P2_Searchlight && isSearchlightFirstPerson && searchlightPitchPivot != null)
                _playerMovement.CameraOverrideTarget = searchlightPitchPivot;
            else
                _playerMovement.CameraOverrideTarget = null;
        }

        bool isHoldingHarpoon = (CurrentMode == EquipmentMode.P3_HarpoonMount);

        if (_harpoonFarming != null)
        {
            _harpoonFarming.hasHarpoon = isHoldingHarpoon;
            _harpoonFarming.enabled = isHoldingHarpoon;
        }

        if (_raycastWeapon != null)
        {
            _raycastWeapon.hasWeapon = false;
            _raycastWeapon.enabled = false;
        }

        if (crosshairImage != null)
        {
            crosshairImage.enabled = isHoldingHarpoon;
            if (isHoldingHarpoon) crosshairImage.color = Color.cyan;
        }
    }

    private void Update()
    {
        if (!HasStateAuthority) return;

        if (Input.GetKeyDown(KeyCode.Alpha1))
        {
            CurrentMode = EquipmentMode.None;
            isSearchlightFirstPerson = false;
            ApplyModeSettings();
        }
        else if (Input.GetKeyDown(KeyCode.Alpha2))
        {
            if (CurrentMode != EquipmentMode.P2_Searchlight)
            {
                CurrentMode = EquipmentMode.P2_Searchlight;
                isSearchlightFirstPerson = false;
                ApplyModeSettings();
            }
        }
        else if (Input.GetKeyDown(KeyCode.Alpha3))
        {
            CurrentMode = EquipmentMode.P3_HarpoonMount;
            isSearchlightFirstPerson = false;
            ApplyModeSettings();
        }

        if (CurrentMode == EquipmentMode.P2_Searchlight)
        {
            if (Input.GetKeyDown(KeyCode.C) || Input.GetMouseButtonDown(1))
            {
                isSearchlightFirstPerson = !isSearchlightFirstPerson;
                ApplyModeSettings();
            }
        }
    }

    private void LateUpdate()
    {
        if (!_initialized || _playerMovement == null) return;

        ApplyModeSettings();

        if (CurrentMode == EquipmentMode.None)
        {
            if (_bodyRig != null) _bodyRig.weight = 0f;
            if (searchlightLamp != null) searchlightLamp.enabled = false;

            if (_animator != null && _animator.layerCount > 1)
                _animator.SetLayerWeight(1, 0f);

            return;
        }
        else
        {
            if (_animator != null && _animator.layerCount > 1)
                _animator.SetLayerWeight(1, 1f);
        }

        float yaw = transform.eulerAngles.y;
        float pitch = _playerMovement.CameraPitch;

        if (invertYaw) yaw = -yaw;
        if (invertPitch) pitch = -pitch;

        if (CurrentMode == EquipmentMode.P2_Searchlight)
        {
            if (searchlightYawPivot != null)
                searchlightYawPivot.localRotation = _initSearchlightYaw * Quaternion.Euler(yawAxis * yaw);

            if (searchlightPitchPivot != null)
                searchlightPitchPivot.localRotation = _initSearchlightPitch * Quaternion.Euler(pitchAxis * pitch);

            FlashlightToggle toggle = GetComponent<FlashlightToggle>();
            if (toggle != null && searchlightLamp != null)
                searchlightLamp.enabled = toggle.IsLightOn;

            Transform targetStation = searchlightControlDesk != null ? searchlightControlDesk : searchlightPitchPivot;
            if (targetStation != null)
            {
                float dist = Vector2.Distance(new Vector2(transform.position.x, transform.position.z), new Vector2(targetStation.position.x, targetStation.position.z));
                if (_bodyRig != null) _bodyRig.weight = (dist <= maxGrabDistance) ? 1f : 0f;

                Vector3 deskTop = targetStation.position + Vector3.up * 0.9f + transform.TransformDirection(gripPositionOffset);
                if (leftHandTarget != null)
                    leftHandTarget.SetPositionAndRotation(deskTop - transform.right * handSpread, transform.rotation * Quaternion.Euler(leftHandRotOffset));
                if (rightHandTarget != null)
                    rightHandTarget.SetPositionAndRotation(deskTop + transform.right * handSpread, transform.rotation * Quaternion.Euler(rightHandRotOffset));
            }
        }
        else if (CurrentMode == EquipmentMode.P3_HarpoonMount)
        {
            if (searchlightLamp != null) searchlightLamp.enabled = false;

            float finalWeaponYaw = yaw + weaponYawOffset;

            if (weaponRootYaw != null)
                weaponRootYaw.localRotation = _initWeaponYaw * Quaternion.Euler(yawAxis * finalWeaponYaw);

            if (weaponAimPivot != null)
                weaponAimPivot.localRotation = _initWeaponPitch * Quaternion.Euler(pitchAxis * pitch);

            if (weaponGrip != null)
            {
                float dist = Vector2.Distance(new Vector2(transform.position.x, transform.position.z), new Vector2(weaponGrip.position.x, weaponGrip.position.z));
                if (_bodyRig != null) _bodyRig.weight = (dist <= maxGrabDistance) ? 1f : 0f;

                Vector3 baseGripPos = weaponGrip.position + transform.TransformDirection(gripPositionOffset);

                if (leftHandTarget != null)
                    leftHandTarget.SetPositionAndRotation(baseGripPos - transform.right * handSpread, transform.rotation * Quaternion.Euler(leftHandRotOffset));
                if (rightHandTarget != null)
                    rightHandTarget.SetPositionAndRotation(baseGripPos + transform.right * handSpread, transform.rotation * Quaternion.Euler(rightHandRotOffset));
            }
        }

        if (lookTarget != null)
            lookTarget.position = transform.position + transform.forward * 5f + Vector3.up * 1.2f;
    }
}

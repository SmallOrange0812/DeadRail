using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Animations.Rigging;
using UnityEngine.UI;
using Fusion;

public class EquipmentPivotController : NetworkBehaviour
{
    public enum EquipmentMode { None = 0, P2_Searchlight = 1, P3_HarpoonMount = 2 }

    [Header("현재 잡고 있는 장비 모드 (1=맨손, 2=탐조등조종, 3=작살잡기)")]
    [Networked] public EquipmentMode CurrentMode { get; set; } = EquipmentMode.None;

    [Header("탐조등 모드 시점 전환 (2번 상태에서 C키 또는 우클릭으로 전환!)")]
    public bool isSearchlightFirstPerson = false;

    private PlayerMovement _playerMovement;
    private HarpoonFarming _harpoonFarming;
    private RaycastWeapon _raycastWeapon;
    private Rig _bodyRig;
    private Image _canvasCrosshairImage;

    [Header("P2 탐조등 피벗 & 손잡이 (자동 검색)")]
    public Transform searchlightYawPivot;   // 지붕 위 _P2_Searchlight_YawPivot.002
    public Transform searchlightPitchPivot; // 지붕 위 Searchlight_PitchPivot.002
    public Transform searchlightControlDesk; // 실내 P2 조종석 (_P2_Searchlight_Control)
    public Light searchlightLamp;

    [Header("거치형 작살/무기 피벗 & 손잡이 (자동 검색)")]
    public Transform weaponRootYaw;         // _P3_Weapon_Mount_ROOT.002
    public Transform weaponAimPivot;        // P3_AimPivot.002
    public Transform weaponGrip;            // Weapon_Grip.002

    [Header("내 캐릭터 IK 타겟 (자동 연결)")]
    public Transform leftHandTarget;        // LeftHandTarget
    public Transform rightHandTarget;       // RightHandTarget
    public Transform lookTarget;            // LookTarget

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
                ik.data.root = lArm;
                ik.data.mid = lForeArm;
                ik.data.tip = lHand;
                ik.data.target = leftHandTarget;
                ik.data.targetPositionWeight = 1f;
                ik.data.targetRotationWeight = 1f;
            }
            else if (ik.name.Contains("Right"))
            {
                ik.data.root = rArm;
                ik.data.mid = rForeArm;
                ik.data.tip = rHand;
                ik.data.target = rightHandTarget;
                ik.data.targetPositionWeight = 1f;
                ik.data.targetRotationWeight = 1f;
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

        Debug.Log("✅ [AutoSetup] 양팔 뼈대 및 상체 IK 타겟 자동 연결 완료!");
    }

    public override void Spawned()
    {
        _playerMovement = GetComponent<PlayerMovement>();
        _harpoonFarming = GetComponent<HarpoonFarming>();
        _raycastWeapon = GetComponent<RaycastWeapon>();
        _bodyRig = GetComponentInChildren<Rig>(true);

        Canvas childCanvas = GetComponentInChildren<Canvas>(true);
        if (childCanvas != null)
        {
            _canvasCrosshairImage = childCanvas.GetComponentInChildren<Image>(true);
        }

        AutoSetupRigAndBones();
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
            if (searchlightYawPivot == null && t.name.Contains("Searchlight_YawPivot"))
                searchlightYawPivot = t;
            else if (searchlightPitchPivot == null && t.name.Contains("Searchlight_PitchPivot"))
                searchlightPitchPivot = t;
            else if (searchlightControlDesk == null && t.name.Contains("_P2_Searchlight_Control"))
                searchlightControlDesk = t;
            else if (weaponRootYaw == null && t.name.Contains("P3_Weapon_Mount_ROOT"))
                weaponRootYaw = t;
            else if (weaponAimPivot == null && t.name.Contains("P3_AimPivot"))
                weaponAimPivot = t;
            else if (weaponGrip == null && t.name.Contains("Weapon_Grip"))
                weaponGrip = t;
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
            }
        }

        if (weaponRootYaw != null) _initWeaponYaw = weaponRootYaw.localRotation;
        if (weaponAimPivot != null) _initWeaponPitch = weaponAimPivot.localRotation;

        _initialized = true;
    }

    private void ApplyModeSettings()
    {
        if (!HasStateAuthority) return;

        // 1. 탐조등 모드(2번)에서 C키/우클릭으로 '탐조등 1인칭'을 켰을 때만 지붕 위 탐조등 시점으로 이동!
        if (_playerMovement != null)
        {
            if (CurrentMode == EquipmentMode.P2_Searchlight && isSearchlightFirstPerson && searchlightPitchPivot != null)
                _playerMovement.CameraOverrideTarget = searchlightPitchPivot;
            else
                _playerMovement.CameraOverrideTarget = null;
        }

        // 2. 오직 3번(작살 잡기)일 때만 민트색 조준점 표시 및 작살 발사 활성화!
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

        if (_canvasCrosshairImage != null)
        {
            _canvasCrosshairImage.enabled = isHoldingHarpoon;
            if (isHoldingHarpoon)
            {
                _canvasCrosshairImage.color = Color.cyan;
            }
        }
    }

    private void Update()
    {
        if (!HasStateAuthority || Keyboard.current == null) return;

        // 숫자 1번: 맨손 / 숫자 2번: 탐조등 조종 / 숫자 3번: 작살 잡기
        if (Keyboard.current.digit1Key.wasPressedThisFrame)
        {
            CurrentMode = EquipmentMode.None;
            isSearchlightFirstPerson = false;
            ApplyModeSettings();
        }
        else if (Keyboard.current.digit2Key.wasPressedThisFrame)
        {
            if (CurrentMode != EquipmentMode.P2_Searchlight)
            {
                CurrentMode = EquipmentMode.P2_Searchlight;
                isSearchlightFirstPerson = false; // 처음 2번을 누르면 플레이어 1인칭으로 시작
            }
            else
            {
                // 이미 2번인 상태에서 또 2번을 누르면 플레이어 1인칭 <-> 탐조등 1인칭 전환!
                isSearchlightFirstPerson = !isSearchlightFirstPerson;
            }
            ApplyModeSettings();
        }
        else if (Keyboard.current.digit3Key.wasPressedThisFrame)
        {
            CurrentMode = EquipmentMode.P3_HarpoonMount;
            isSearchlightFirstPerson = false;
            ApplyModeSettings();
        }

        // 2번(탐조등 모드)일 때 C키 또는 마우스 우클릭을 눌러도 플레이어 1인칭 <-> 탐조등 1인칭 즉시 전환!
        if (CurrentMode == EquipmentMode.P2_Searchlight)
        {
            bool toggleCam = Keyboard.current.cKey.wasPressedThisFrame ||
                             (Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame);

            if (toggleCam)
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
            return;
        }

        float yaw = transform.eulerAngles.y;
        float pitch = _playerMovement.CameraPitch;

        if (invertYaw) yaw = -yaw;
        if (invertPitch) pitch = -pitch;

        // [숫자 2번] 탐조등 조종 모드
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
        // [숫자 3번] 작살 잡기 모드
        else if (CurrentMode == EquipmentMode.P3_HarpoonMount)
        {
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
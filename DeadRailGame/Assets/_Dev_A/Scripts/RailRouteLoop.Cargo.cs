using Fusion;
using UnityEngine;

public sealed partial class RailRouteLoop
{
    private const int CargoDoorMaxHealth = 100;
    [Networked] private int CargoDoorHealth { get; set; }
    [Networked] private float LastCargoFuelLoss { get; set; }
    [Networked] private PlayerRef CargoFuelCarrier { get; set; }
    [Networked] private NetworkBool FirstFuelDelivered { get; set; }
    [Networked] private NetworkBool FuelDefenseTriggered { get; set; }
    [Networked] private TickTimer DefenseDelay { get; set; }
    [Networked] private NetworkBool FirstDefenseBreached { get; set; }

    private GameObject carriedFuelVisual;
    private SearchlightSignboard entrySpeedSign;
    private TextMesh cargoDoorMarker;

    internal static RailEntryOutcome EvaluateEntry(RailRoute route, float actualSpeed, int requiredSpeed)
    {
        float error = Mathf.Abs(actualSpeed - requiredSpeed);
        if (route == RailRoute.Straight || error > 15f) return RailEntryOutcome.Blackout;
        return error > 5f ? RailEntryOutcome.CargoDamaged : RailEntryOutcome.Normal;
    }

    private Vector3 CargoDoorPoint => trainTransform.TransformPoint(new Vector3(0f, 2.2f, boardingBounds.min.z + 0.45f));
    // Attack from the platform beside the rear of the train.
    private Vector3 CargoAttackPoint => trainTransform.TransformPoint(
        new Vector3(boardingBounds.max.x + 0.8f, 0f, boardingBounds.min.z - 0.1f));

    private void DamageCargoDoor(int damage)
    {
        if (!HasStateAuthority || CargoDoorHealth <= 0 || damage <= 0) return;
        CargoDoorHealth = Mathf.Max(0, CargoDoorHealth - damage);
        if (CargoDoorHealth != 0) return;
        LastCargoFuelLoss = TreadmillManager.Instance.LoseCargoFuel(10f);
        if (VisitNumber == 1 && FuelDefenseTriggered) FirstDefenseBreached = true;
    }

    private void UpdateCargoCarrier()
    {
        if (CargoFuelCarrier == PlayerRef.None) return;
        if (Runner.TryGetPlayerObject(CargoFuelCarrier, out NetworkObject player) &&
            player.TryGetComponent(out PlayerMovement movement) && !movement.IsDowned) return;

        CargoFuelCarrier = PlayerRef.None;
        // Return an abandoned can to its authored pickup point.
        for (int i = 0; i < supplyPoints.Length && i < 30; i++)
            if (GetSupplyKind(i) == FarmingSupplyKind.Fuel) CollectedSupplies &= ~(1 << i);
    }

    private bool FirstDefensePending()
    {
        if (VisitNumber != 1 || !FuelDefenseTriggered) return false;
        if (!FirstFuelDelivered) return true;
        if (FirstDefenseBreached) return false;
        if (!DefenseDelay.Expired(Runner) || (zombiePrefab != null && EncounterVisit != VisitNumber)) return true;
        for (int i = 0; i < ZombieCount; i++)
            if (Zombies[i].Health > 0) return true;
        return false;
    }

    private string CargoStatus()
    {
        string status = CargoDoorHealth == 0 ? "후방 화물문 파손" : $"후방 화물문 {CargoDoorHealth}/{CargoDoorMaxHealth}";
        if (LastCargoFuelLoss > 0f) status += $" · 파손 시 연료 −{LastCargoFuelLoss:0.#}";
        if (VisitNumber != 1 || Phase != RailTravelPhase.Farming) return status;
        if (!FuelDefenseTriggered) return $"{status} · 연료통을 들면 방어 시작";
        float remaining = DefenseDelay.RemainingTime(Runner) ?? 0f;
        if (remaining > 0f) return $"{status} · {Mathf.CeilToInt(remaining)}초 뒤 좀비 습격";
        if (FirstDefenseBreached) return $"{status} · 방어 실패";
        return FirstDefensePending() ? $"{status} · 방어 중" : $"{status} · 방어 완료";
    }

    private bool CanReachCargoDoor(Vector3 position) => IsAboard(position) && Vector3.Distance(position, CargoDoorPoint) <= 3f;

    internal bool TryInteractCargo(PlayerMovement player)
    {
        if (!Ready || Phase != RailTravelPhase.Farming || trainTransform == null ||
            !CanReachCargoDoor(player.transform.position)) return false;
        if (CargoFuelCarrier != Runner.LocalPlayer && (CargoDoorHealth >= CargoDoorMaxHealth || player.InventoryRepairKits <= 0))
            return false;
        Rpc_InteractCargoDoor();
        return true;
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void Rpc_InteractCargoDoor(RpcInfo info = default)
    {
        if (Phase != RailTravelPhase.Farming || trainTransform == null) return;
        PlayerRef sender = info.Source == PlayerRef.None ? Runner.LocalPlayer : info.Source;
        if (sender == PlayerRef.None || !Runner.TryGetPlayerObject(sender, out NetworkObject player) ||
            !player.TryGetComponent(out PlayerMovement movement) || movement.IsDowned ||
            !CanReachCargoDoor(player.transform.position)) return;

        TreadmillManager train = TreadmillManager.Instance;
        if (CargoFuelCarrier == sender)
        {
            train.CollectSupply(FarmingSupplyKind.Fuel, supplyAmount);
            FirstFuelDelivered = true;
            CargoFuelCarrier = PlayerRef.None;
        }
        else if (CargoDoorHealth < CargoDoorMaxHealth && movement.TryConsumeRepairKit())
        {
            CargoDoorHealth = CargoDoorMaxHealth;
            train.RecordRepairKitUsed();
        }
    }

    private string CargoPrompt()
    {
        if (Phase != RailTravelPhase.Farming || trainTransform == null ||
            !Runner.TryGetPlayerObject(Runner.LocalPlayer, out NetworkObject player) ||
            !player.TryGetComponent(out PlayerMovement movement) || movement.IsDowned) return null;

        bool reach = CanReachCargoDoor(player.transform.position);
        if (CargoFuelCarrier != PlayerRef.None && CargoFuelCarrier == Runner.LocalPlayer)
            return reach ? "[F] 연료통 적재" : "연료통 운반 중 · 열차 후방 화물문에서 [F]로 적재";
        return reach && CargoDoorHealth < CargoDoorMaxHealth
            ? (movement.InventoryRepairKits > 0 ? "[F] 수리품 1개로 화물문 복구" : "화물문 복구에 수리품 1개 필요")
            : null;
    }

    private void RenderCarriedFuel()
    {
        if (Phase == RailTravelPhase.Farming && CargoFuelCarrier != PlayerRef.None && smallFuelCan != null &&
            Runner.TryGetPlayerObject(CargoFuelCarrier, out NetworkObject carrier))
        {
            if (carriedFuelVisual == null) carriedFuelVisual = Instantiate(smallFuelCan, transform);
            carriedFuelVisual.transform.SetPositionAndRotation(
                carrier.transform.TransformPoint(new Vector3(0.45f, 0.4f, 0.4f)), carrier.transform.rotation);
            carriedFuelVisual.SetActive(true);
        }
        else if (carriedFuelVisual != null) carriedFuelVisual.SetActive(false);
    }

    private void RenderCargoDoorMarker()
    {
        if (Phase != RailTravelPhase.Farming || trainTransform == null)
        {
            if (cargoDoorMarker != null) cargoDoorMarker.gameObject.SetActive(false);
            return;
        }
        if (cargoDoorMarker == null)
        {
            var sign = new GameObject("Rear cargo door marker");
            sign.transform.SetParent(trainTransform, false);
            sign.transform.localPosition = new Vector3(boardingBounds.max.x + 0.12f, 2.85f, boardingBounds.min.z + 0.45f);
            sign.transform.localRotation = Quaternion.Euler(0f, -90f, 0f);
            cargoDoorMarker = sign.AddComponent<TextMesh>();
            cargoDoorMarker.anchor = TextAnchor.MiddleCenter;
            cargoDoorMarker.fontSize = 60;
            cargoDoorMarker.characterSize = 0.19f;
        }
        cargoDoorMarker.gameObject.SetActive(true);
        cargoDoorMarker.text = CargoDoorHealth == 0 ? "CARGO · BREACHED" : $"CARGO · {CargoDoorHealth}";
        cargoDoorMarker.color = CargoDoorHealth < CargoDoorMaxHealth ? new Color(1f, 0.43f, 0.22f) : Color.white;
    }

    private void RenderEntrySpeedSign()
    {
        bool visible = Phase == RailTravelPhase.Cruise && PhaseDistance <= junctionRevealDistance;
        if (!visible)
        {
            if (entrySpeedSign != null) entrySpeedSign.gameObject.SetActive(false);
            return;
        }
        MapNodeManager nodes = MapNodeManager.Instance;
        if (nodes == null) return;
        if (entrySpeedSign == null)
        {
            var sign = new GameObject("Route entry speed sign");
            sign.transform.SetParent(junctionGeometry, false);
            sign.transform.localPosition = new Vector3(4.1f, 3.25f, -3f);
            GameObject panel = GameObject.CreatePrimitive(PrimitiveType.Cube);
            panel.transform.SetParent(sign.transform, false);
            panel.transform.localScale = new Vector3(3.9f, 1.45f, 0.18f);
            panel.GetComponent<Collider>().enabled = false;
            entrySpeedSign = sign.AddComponent<SearchlightSignboard>();
            entrySpeedSign.usesRouteSpeeds = true;
            entrySpeedSign.boardRenderer = panel.GetComponent<Renderer>();
            if (ammoBoxBody != null) entrySpeedSign.boardRenderer.sharedMaterial = ammoBoxBody;
            var textObject = new GameObject("Route speed text");
            textObject.transform.SetParent(sign.transform, false);
            textObject.transform.localPosition = new Vector3(0f, 0f, -0.11f);
            entrySpeedSign.legacyTextMesh = textObject.AddComponent<TextMesh>();
            entrySpeedSign.textSize = 0.42f;
        }
        entrySpeedSign.gameObject.SetActive(true);
        entrySpeedSign.SetHiddenMessage(
            $"LEFT   {nodes.GetRouteNode(RailRoute.Left).requiredSpeed} km/h\nRIGHT   {nodes.GetRouteNode(RailRoute.Right).requiredSpeed} km/h");
    }
}

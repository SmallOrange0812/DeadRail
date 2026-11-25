using Fusion;
using UnityEngine;

public sealed partial class RailRouteLoop
{
    [Networked] private TickTimer DownedTeamDelay { get; set; }
    [Networked] private NetworkBool Defeated { get; set; }
    private bool presentingDefeat;
    private string retryError;
    internal bool HasRunResult => Ready && Defeated;

    private void UpdateDefeat(TreadmillManager train)
    {
        if (Defeated) return;
        bool hasPlayer = false;
        foreach (PlayerRef player in Runner.ActivePlayers)
        {
            if (!Runner.TryGetPlayerObject(player, out NetworkObject actor) ||
                !actor.TryGetComponent(out PlayerMovement movement) || !movement.IsDowned)
            {
                DownedTeamDelay = default;
                return;
            }
            hasPlayer = true;
        }
        if (!hasPlayer)
        {
            DownedTeamDelay = default;
            return;
        }
        if (!DownedTeamDelay.IsRunning) DownedTeamDelay = TickTimer.CreateFromSeconds(Runner, 4f);
        else if (DownedTeamDelay.Expired(Runner))
        {
            train.StopForDefeat();
            Defeated = true;
        }
    }

    private void RenderRunResult()
    {
        if (presentingDefeat == Defeated) return;
        presentingDefeat = Defeated;
        if (presentingDefeat)
            TerminalUIManager.Instance?.CloseAllTerminals();
        else
            retryError = null;
        if (GamePauseMenu.BlocksGameplayInput) return;
        Cursor.lockState = presentingDefeat ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = presentingDefeat;
    }

    private void OnGUI()
    {
        if (!Ready || !presentingDefeat || GamePauseMenu.BlocksGameplayInput) return;
        Rect panel = new Rect((Screen.width - 380f) * 0.5f, (Screen.height - 188f) * 0.5f, 380f, 188f);
        GUI.Box(panel, "운행 실패");
        GUI.Label(new Rect(panel.x + 20, panel.y + 50, panel.width - 40, 25), "팀원 전원이 다운됐습니다.");
        if (HasStateAuthority)
        {
            if (GUI.Button(new Rect(panel.x + 20, panel.y + 86, panel.width - 40, 38), "같은 파티로 재도전"))
                RetryRun();
            if (retryError != null)
                GUI.Label(new Rect(panel.x + 20, panel.y + 134, panel.width - 40, 28), retryError);
        }
        else
        {
            GUI.Label(new Rect(panel.x + 20, panel.y + 92, panel.width - 40, 25), "호스트의 재도전을 기다리는 중...");
        }
    }

    private void RetryRun()
    {
        if (!Defeated || !HasStateAuthority) return;
        NetworkManager game = FindFirstObjectByType<NetworkManager>();
        if (game == null || !game.TryResetRunPlayers(Runner))
        {
            retryError = "기지 스폰 지점을 확인할 수 없습니다.";
            return;
        }

        TreadmillManager.Instance.ResetTrainForRun();
        ResetRouteForRun();
        MapNodeManager.Instance?.BeginRouteStage(1);
    }

    private void ResetRouteForRun()
    {
        Phase = RailTravelPhase.Cruise;
        PhaseDistance = cruisingDistance;
        TravelledDistance = 0f;
        TransitionTime = 0f;
        SelectedRoute = RailRoute.Straight;
        LockedRoute = RailRoute.Left;
        VisitNumber = 1;
        ActiveMapIndex = -1;
        ActiveMapDanger = 0;
        BranchEntryFailed = false;
        EntryOutcome = RailEntryOutcome.Normal;
        DepartureWaiting = false;
        CollectedSupplies = 0;
        EncounterVisit = 0;
        ZombieCount = 0;
        CargoDoorHealth = CargoDoorMaxHealth;
        LastCargoFuelLoss = 0f;
        CargoFuelCarrier = PlayerRef.None;
        FirstFuelDelivered = false;
        FuelDefenseTriggered = false;
        DefenseDelay = default;
        FirstDefenseBreached = false;
        DownedTeamDelay = default;
        Defeated = false;
    }
}

using UnityEngine;

public class ConsoleInteractable : MonoBehaviour
{
    public enum ConsoleType { P1_DriveCinsole, P4_RadarConsole, P2_SearchlightConsole }

    [Header("콘솔 종류 및 거리 설정")]
    public ConsoleType consoleType = ConsoleType.P1_DriveCinsole;
    [Tooltip("상호작용 가능한 최대 거리 (미터)")]
    public float interactDistance = 2.5f;

    private void Awake()
    {
        if (consoleType != ConsoleType.P4_RadarConsole) return;
        foreach (Renderer part in GetComponentsInChildren<Renderer>(true))
        {
            if (part.name == "Radar_Grid_Line" || part.name.StartsWith("Radar_Grid_Line."))
                part.enabled = false;
        }
    }

    public string DisplayName => consoleType switch
    {
        ConsoleType.P1_DriveCinsole => "P1 기관실 속도 제어 콘솔",
        ConsoleType.P2_SearchlightConsole => "P2 탐조등 콘솔",
        _ => "P4 전방 레이더 관제 콘솔"
    };

    public static bool IsConsoleNearestInteraction(PlayerMovement player) => FindAimedConsole(player) != null;

    internal static ConsoleInteractable FindAimedConsole(PlayerMovement player)
    {
        if (!TryGetAimedHit(player, out RaycastHit hit)) return null;
        return GetConsoleFromHit(player, hit);
    }

    internal static ConsoleInteractable GetConsoleFromHit(PlayerMovement player, RaycastHit hit)
    {
        ConsoleInteractable console = hit.collider.GetComponentInParent<ConsoleInteractable>();
        return console != null && console.isActiveAndEnabled
            && Vector3.Distance(player.fpsCamera.transform.position, hit.point) <= console.interactDistance
            ? console
            : null;
    }

    internal static bool TryGetAimedHit(PlayerMovement player, out RaycastHit hit)
    {
        hit = default;
        if (player == null || player.Object == null || !player.Object.IsValid || !player.IsLocalPlayer
            || player.IsClimbingLadder || player.CameraOverrideTarget != null)
            return false;

        Camera camera = player.fpsCamera;
        if (camera == null || !camera.isActiveAndEnabled)
            return false;

        Ray ray = camera.ViewportPointToRay(new Vector3(0.5f, 0.5f));
        if (!Physics.Raycast(ray, out hit, Mathf.Infinity, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide))
            return false;

        if (!IsAimSurface(hit.collider, player))
        {
            hit = default;
            float nearest = float.PositiveInfinity;
            foreach (RaycastHit candidate in Physics.RaycastAll(ray, Mathf.Infinity, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide))
            {
                if (candidate.distance >= nearest || !IsAimSurface(candidate.collider, player))
                    continue;
                hit = candidate;
                nearest = candidate.distance;
            }
        }

        return hit.collider != null;
    }

    private static bool IsAimSurface(Collider collider, PlayerMovement player)
    {
        return !collider.transform.IsChildOf(player.transform) &&
            (!collider.isTrigger || collider.GetComponentInParent<ConsoleInteractable>() != null ||
                IsLadderPart(collider.name));
    }

    internal static bool IsLadderPart(string name) =>
        name == "Roof_Access_Ladder" || name.StartsWith("Roof_Access_Ladder.") ||
        name == "Ladder_Rung" || name.StartsWith("Ladder_Rung.");
}

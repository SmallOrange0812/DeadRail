using UnityEngine;

public sealed class TrainEntryDoors : MonoBehaviour
{
    internal const float SlideSeconds = 0.85f;
    [SerializeField] private Transform leftDoor;
    [SerializeField] private Transform rightDoor;
    [SerializeField] private Transform leftSwitch;
    [SerializeField] private Transform rightSwitch;
    [SerializeField] private float slideDistance = 1.47f;

    private Vector3 leftClosedPosition;
    private Vector3 rightClosedPosition;

    private void Awake()
    {
        // The scene stores the fitted positions across the doorway.
        leftClosedPosition = leftDoor.localPosition;
        rightClosedPosition = rightDoor.localPosition;
        Present(Vector2.zero);
    }

    internal void Present(Vector2 closure)
    {
        leftDoor.localPosition = leftClosedPosition + LocalSlide(leftDoor, closure.x);
        rightDoor.localPosition = rightClosedPosition + LocalSlide(rightDoor, closure.y);
    }

    private Vector3 LocalSlide(Transform door, float closure)
    {
        return door.parent.InverseTransformVector(transform.forward * (slideDistance * Mathf.SmoothStep(1f, 0f, closure)));
    }

    internal bool CanUseSwitch(int index, Vector3 playerPosition)
    {
        Transform panel = index == 0 ? leftSwitch : rightSwitch;
        Vector3 player = transform.InverseTransformPoint(playerPosition);
        return Mathf.Abs(player.x) < 1.85f && Vector3.Distance(playerPosition, panel.position) <= 2.15f;
    }
}

using UnityEngine;

public sealed class TrainDoorSwitch : MonoBehaviour
{
    [SerializeField, Range(0, 1)] private int doorIndex;
    [SerializeField] private bool opensDoor;

    internal bool IsInReach(Vector3 playerPosition)
    {
        TreadmillManager train = TreadmillManager.Instance;
        return train != null && train.CanUseDoorSwitch(doorIndex, playerPosition);
    }

    internal string Prompt
    {
        get
        {
            TreadmillManager train = TreadmillManager.Instance;
            if (train == null || !train.HasSpawned) return string.Empty;
            if (train.EntryDoorsLocked) return "정차 후 출입문 조작 가능";
            return opensDoor ? "[F]  출입문 열기" : "[F]  출입문 닫기";
        }
    }

    internal void Interact()
    {
        TreadmillManager train = TreadmillManager.Instance;
        if (train != null && train.HasSpawned && !train.EntryDoorsLocked)
            train.Rpc_SetEntryDoor(doorIndex, opensDoor);
    }
}

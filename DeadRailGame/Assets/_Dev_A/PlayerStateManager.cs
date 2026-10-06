using UnityEngine;
using Fusion;

public enum PlayerMode { ActionMode, UIMode }

public class PlayerStateManager : NetworkBehaviour
{
    public PlayerMode CurrentMode = PlayerMode.ActionMode;

    public GameObject uiCanvas;
    public GameObject player3DModel;
    public MonoBehaviour movementScript;

    public override void Spawned()
    {
        if (HasStateAuthority)
        {
            CameraFollow camFollow = Camera.main.gameObject.GetComponent<CameraFollow>();
            if (camFollow == null)
            {
                camFollow = Camera.main.gameObject.AddComponent<CameraFollow>();
            }
            camFollow.target = this.transform;
        }
        UpdateStateVisuals();
    }

    void Update()
    {
        if (!HasStateAuthority) return;

        if (Input.GetKeyDown(KeyCode.Tab))
        {
            CurrentMode = (CurrentMode == PlayerMode.ActionMode) ? PlayerMode.UIMode : PlayerMode.ActionMode;
            UpdateStateVisuals();
        }
    }

    private void UpdateStateVisuals()
    {
        bool isActionMode = (CurrentMode == PlayerMode.ActionMode);

        if (player3DModel != null) player3DModel.SetActive(isActionMode);
        if (movementScript != null) movementScript.enabled = isActionMode;

        if (uiCanvas != null)
        {
            uiCanvas.SetActive(HasStateAuthority && !isActionMode);
        }
    }
}
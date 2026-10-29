using UnityEngine;
using Fusion;

public enum PlayerMode { ActionMode, UIMode }

public class PlayerStateManager : NetworkBehaviour
{
    public override void Spawned()
    {
        if(Camera.main != null)
        {
            if(HasStateAuthority)
            {
                CameraFollow camFollow = Camera.main.gameObject.GetComponent<CameraFollow>();
                if (camFollow == null)
                {
                    camFollow = Camera.main.gameObject.AddComponent<CameraFollow>();
                }
                camFollow.target = this.transform;
            }
        }
    }
}
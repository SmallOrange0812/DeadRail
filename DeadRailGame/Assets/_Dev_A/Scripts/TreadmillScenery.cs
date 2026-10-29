using UnityEngine;

public class TreadmillScenery : MonoBehaviour
{
    [Header("Pooling Settings")]
    public float despawnZ = -100f;
    public float respawnZ = 100f;

    void Update()
    {
        // 수정: 매니저가 없거나, 아직 네트워크에 스폰되지 않았거나, 속도가 0이면 대기 (에러 방지)
        if (TreadmillManager.Instance == null || !TreadmillManager.Instance.isSpawned || TreadmillManager.Instance.CurrentSpeed <= 0f)
            return;

        float speed = TreadmillManager.Instance.CurrentSpeed;
        transform.Translate(Vector3.back * speed * Time.deltaTime, Space.World);

        if (transform.position.z <= despawnZ)
        {
            float moveDistance = respawnZ - despawnZ;
            transform.position += new Vector3(0, 0, moveDistance);
        }
    }
}
using UnityEngine;

public class CameraTargetManager : MonoBehaviour
{
    public static CameraTargetManager Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null)
        {
            Debug.LogWarning("CameraTargetManager单例重复实例化");
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }
}
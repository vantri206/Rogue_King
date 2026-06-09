using UnityEngine;

public class SingletonMB<T> : MonoBehaviour where T : MonoBehaviour
{
    private static bool isShuttingDown = false;
    private static object _lock = new object();

    private static T instance;

    public static T Instance
    {
        get
        {
            if (isShuttingDown)
            {
                Debug.LogWarning($"[SingletonMB] Instance '{typeof(T)}' already destroyed. Return null.");
                return null;
            }
            lock (_lock)
            {
                if (instance == null)
                {
                    instance = (T)FindFirstObjectByType(typeof(T));
                    if (instance == null)
                    {
                        var singletonObject = new GameObject();
                        instance = singletonObject.AddComponent<T>();
                        singletonObject.name = typeof(T).ToString() + " (Singleton)";

                        Debug.Log($"[SingletonMB] '{singletonObject}' was created.");

                        DontDestroyOnLoad(singletonObject);
                    }
                }
                return instance;
            }
        }
    }
    private void OnApplicationQuit()
    {
        isShuttingDown = true;
    }
    private void OnDestroy()
    {
        isShuttingDown = true;
    }
}

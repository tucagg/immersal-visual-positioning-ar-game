using Firebase;
using Firebase.Extensions;
using Firebase.Database;
using Firebase.Storage;
using UnityEngine;

public class FirebaseInitializer : MonoBehaviour
{
    public static FirebaseInitializer Instance { get; private set; }

    [Header("Config")]
    public string databaseUrl;
    public string storageBucket;

    public static DatabaseReference DB { get; private set; }
    public static FirebaseStorage Storage { get; private set; }
    public static bool Ready { get; private set; }

    private bool _initializing;

    void Awake()
    {
        // Singleton
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        // Zaten hazırsa tekrar init etme
        if (Ready || _initializing) return;

        _initializing = true;
        Ready = false;

        FirebaseApp.CheckAndFixDependenciesAsync().ContinueWithOnMainThread(task =>
        {
            _initializing = false;

            if (task.Result != DependencyStatus.Available)
            {
                Debug.LogError("[Firebase] Dependencies not resolved: " + task.Result);
                return;
            }

            var app = FirebaseApp.DefaultInstance;

            // ✅ Daha doğru overload: app + url
            var db = FirebaseDatabase.GetInstance(app, databaseUrl);
            DB = db.RootReference;

            // ✅ Daha doğru overload: app + bucket
            Storage = FirebaseStorage.GetInstance(app, storageBucket);

            Ready = true;
            Debug.Log("[Firebase] Ready");
        });
    }
}
using System;
using System.IO;
using Firebase.Database;
using Firebase.Extensions;
using UnityEngine;

public class FirebaseSmokeTest : MonoBehaviour
{
    [Tooltip("Storage içindeki test dosyasının yolu, örn: test/hello.txt")]
    public string storageTestPath = "test/hello.txt";

    void Start()
    {
        StartCoroutine(CoRun());
    }

    System.Collections.IEnumerator CoRun()
    {
        // init bekle
        while (!FirebaseInitializer.Ready) yield return null;

        // ---- Realtime Database PING ----
        string pingKey = "smokeTest/ping";
        string pingVal = "hello-" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        FirebaseInitializer.DB.Child(pingKey).SetValueAsync(pingVal).ContinueWithOnMainThread(t =>
        {
            if (t.IsCompletedSuccessfully)
            {
                Debug.Log("[DB] Write OK: " + pingVal);
                FirebaseInitializer.DB.Child(pingKey).GetValueAsync().ContinueWithOnMainThread(rt =>
                {
                    if (rt.IsCompletedSuccessfully && rt.Result.Exists)
                        Debug.Log("[DB] Read OK: " + rt.Result.Value);
                    else
                        Debug.LogError("[DB] Read FAIL");
                });
            }
            else Debug.LogError("[DB] Write FAIL: " + t.Exception);
        });

        // ---- Storage DOWNLOAD ----
        var local = Path.Combine(Application.persistentDataPath, "hello.txt");
        var gsRef = FirebaseInitializer.Storage.GetReference(storageTestPath);
        var task = gsRef.GetFileAsync(local);
        while (!task.IsCompleted) yield return null;

        if (task.IsFaulted || task.IsCanceled) Debug.LogError("[Storage] Download FAIL: " + task.Exception);
        else Debug.Log("[Storage] Download OK: " + local);
    }
}
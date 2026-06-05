#if UNITY_IOS
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;
using System.IO;

public static class iOSBuildPostProcess
{
    [PostProcessBuild(999)]
    public static void OnPostProcessBuild(BuildTarget target, string buildPath)
    {
        if (target != BuildTarget.iOS) return;

        string plistPath = Path.Combine(buildPath, "Info.plist");
        var plist = new PlistDocument();
        plist.ReadFromFile(plistPath);
        var root = plist.root;

        // Konum izin metinleri
        SetIfMissing(root, "NSLocationWhenInUseUsageDescription",
            "Yakınındaki haritalar için konum gerekiyor.");
        SetIfMissing(root, "NSLocationAlwaysAndWhenInUseUsageDescription",
            "Uygulama kapalıyken yakınında yeni harita çıkınca bildirim almak için konum gerekiyor.");
        SetIfMissing(root, "NSLocationAlwaysUsageDescription",
            "Uygulama kapalıyken yakınında yeni harita çıkınca bildirim almak için konum gerekiyor.");

        // Background Modes: location
        var bgModes = root["UIBackgroundModes"] as PlistElementArray
                   ?? root.CreateArray("UIBackgroundModes");
        bool hasLocation = false;
        foreach (var el in bgModes.values)
            if (el.AsString() == "location") { hasLocation = true; break; }
        if (!hasLocation) bgModes.AddString("location");

        plist.WriteToFile(plistPath);

        UnityEngine.Debug.Log("[iOSBuild] Info.plist güncellendi.");
    }

    static void SetIfMissing(PlistElementDict dict, string key, string value)
    {
        if (dict[key] == null) dict.SetString(key, value);
    }
}
#endif

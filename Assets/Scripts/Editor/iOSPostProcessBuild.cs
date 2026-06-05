#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;
using System.IO;

/// <summary>
/// Automatically adds required iOS permissions to Info.plist after every iOS build.
/// Currently adds NSPhotoLibraryUsageDescription so the image picker works.
/// </summary>
public static class iOSPostProcessBuild
{
    [PostProcessBuild(999)]
    public static void OnPostProcessBuild(BuildTarget target, string buildPath)
    {
        if (target != BuildTarget.iOS)
            return;

        string plistPath = Path.Combine(buildPath, "Info.plist");
        var plist = new PlistDocument();
        plist.ReadFromFile(plistPath);

        PlistElementDict root = plist.root;

        // Photo library access (read) — required for UIImagePickerController.
        const string photoKey = "NSPhotoLibraryUsageDescription";
        if (!root.values.ContainsKey(photoKey))
        {
            root.SetString(photoKey,
                "Who8 uses your photo library so you can set a profile picture.");
        }

        // Photo library add (write) — required on iOS 11+ if you ever save photos.
        const string addKey = "NSPhotoLibraryAddUsageDescription";
        if (!root.values.ContainsKey(addKey))
        {
            root.SetString(addKey,
                "Who8 may save images to your photo library.");
        }

        plist.WriteToFile(plistPath);
        UnityEngine.Debug.Log("[iOSPostProcessBuild] Info.plist updated with photo permissions.");
    }
}
#endif

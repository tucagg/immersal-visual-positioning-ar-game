using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

/// <summary>
/// App-wide in-memory sprite cache for remote photos.
///
/// Usage (anywhere, no MonoBehaviour needed):
///   ImageCache.Load(url, sprite => myImage.sprite = sprite);
///
/// - Returns cached sprite instantly on subsequent calls with the same URL.
/// - If the same URL is requested while already downloading, the new callback
///   is queued and fired once the single download completes (no duplicate requests).
/// - Cache survives scene loads (DontDestroyOnLoad).
/// - Call ImageCache.Clear() to free memory (e.g. on logout).
/// </summary>
public class ImageCache : MonoBehaviour
{
    // ── Singleton ────────────────────────────────────────────────────────────

    private static ImageCache _instance;

    private static ImageCache Instance
    {
        get
        {
            if (_instance != null) return _instance;

            var go = new GameObject("[ImageCache]");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<ImageCache>();
            return _instance;
        }
    }

    // ── Storage ──────────────────────────────────────────────────────────────

    // url → ready sprite
    private static readonly Dictionary<string, Sprite> _cache = new();

    // url → callbacks waiting for an in-flight download to finish
    private static readonly Dictionary<string, List<Action<Sprite>>> _pending = new();

    // ── Public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// Loads a remote image. Calls <paramref name="onLoaded"/> with the sprite
    /// as soon as it is available (immediately if cached, async otherwise).
    /// Safe to call from any MonoBehaviour — no coroutine needed at the call site.
    /// </summary>
    public static void Load(string url, Action<Sprite> onLoaded)
    {
        if (string.IsNullOrEmpty(url) || onLoaded == null) return;

        // 1. Already cached → instant callback.
        if (_cache.TryGetValue(url, out var cached))
        {
            onLoaded(cached);
            return;
        }

        // 2. Already downloading → queue the callback.
        if (_pending.TryGetValue(url, out var queue))
        {
            queue.Add(onLoaded);
            return;
        }

        // 3. New download.
        _pending[url] = new List<Action<Sprite>> { onLoaded };
        Instance.StartCoroutine(Instance.Download(url));
    }

    /// <summary>Removes all cached sprites to free memory.</summary>
    public static void Clear()
    {
        _cache.Clear();
        // Don't clear _pending — in-flight downloads will still resolve.
        Debug.Log("[ImageCache] Cache cleared.");
    }

    /// <summary>Removes a single URL from the cache (forces re-download next time).</summary>
    public static void Invalidate(string url)
    {
        if (!string.IsNullOrEmpty(url))
            _cache.Remove(url);
    }

    // ── Internal download ─────────────────────────────────────────────────────

    private IEnumerator Download(string url)
    {
        using var req = UnityWebRequestTexture.GetTexture(url);
        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
        {
            Debug.LogWarning($"[ImageCache] Download failed for {url}: {req.error}");
            _pending.Remove(url);
            yield break;
        }

        Texture2D tex = DownloadHandlerTexture.GetContent(req);
        if (tex == null)
        {
            _pending.Remove(url);
            yield break;
        }

        var sprite = Sprite.Create(
            tex,
            new Rect(0, 0, tex.width, tex.height),
            new Vector2(0.5f, 0.5f)
        );

        _cache[url] = sprite;

        // Fire all queued callbacks.
        if (_pending.TryGetValue(url, out var callbacks))
        {
            _pending.Remove(url);
            foreach (var cb in callbacks)
                cb?.Invoke(sprite);
        }
    }
}

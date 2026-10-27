using System;
using UnityEngine;
using ColorMatcher.Core;

public static class Hearts
{
    public const string StorageKey = "chromadrop_hearts_v1";
    public static string Notice;
    static long Now { get { return DateTimeOffset.UtcNow.ToUnixTimeSeconds(); } }

    static HeartState Load()
    {
        string json = PlayerPrefs.GetString(StorageKey, "");
        var state = json.Length == 0 ? new HeartState() : JsonUtility.FromJson<HeartState>(json);
        int count = state == null ? -1 : state.count;
        long next = state == null ? -1 : state.nextUtc;
        HeartClock.Recover(state, Now);
        if (json.Length == 0 || count != state.count || next != state.nextUtc) Save(state);
        return state;
    }

    static void Save(HeartState state)
    {
        bool existed = PlayerPrefs.HasKey(StorageKey);
        string before = PlayerPrefs.GetString(StorageKey, "");
        try { PlayerPrefs.SetString(StorageKey, JsonUtility.ToJson(state)); PlayerPrefs.Save(); }
        catch
        {
            if (existed) PlayerPrefs.SetString(StorageKey, before); else PlayerPrefs.DeleteKey(StorageKey);
            throw;
        }
    }

    public static int Count { get { return Load().count; } }
    public static int SecondsToNext
    {
        get { var state = Load(); return state.count == HeartClock.Capacity ? 0 : (int)Math.Max(0, state.nextUtc - Now); }
    }

    public static bool TrySpend()
    {
        var state = Load();
        if (!HeartClock.Spend(state, Now)) return false;
        Save(state);
        return true;
    }

    public static void Add(int amount)
    {
        var state = Load(); HeartClock.Add(state, amount, Now); Save(state);
    }
}

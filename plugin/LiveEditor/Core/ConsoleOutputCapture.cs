using HarmonyLib;
using Il2CppInterop.Runtime;
using TheForest;
using UnityEngine;

namespace LiveEditor.Core;

/// <summary>
/// Captures the text a console command prints.
///
/// First attempt only hooked LogCommandInfo/Running/Failed, which came back empty for
/// help and the list* commands - most commands do not use those helpers at all. The
/// console's real sink is a _logs queue of LogContent fed by LogCallback (the Unity
/// log callback), so the dependable approach is to diff that queue around a command
/// instead of guessing which logging helper a command happens to call.
///
/// The queue diff needs no Harmony at all, which also means it still works if IL2CPP
/// inlines the log helpers and the patches below never attach. The patches remain as
/// a supplement and their output is merged in.
/// </summary>
public static class ConsoleOutputCapture
{
    private static readonly List<string> _patchBuffer = new();
    private static readonly object _lock = new();
    private static bool _capturing;
    private static int _logCountAtStart;
    private static Application.LogCallback? _unityHook;
    public static bool UnityHookInstalled { get; private set; }

    /// <summary>
    /// Subscribes to Unity's log stream directly.
    ///
    /// This is attempt three, and the reason the first two failed: the console only
    /// registers its own LogCallback while it is enabled, so with the console UI shut
    /// (which is how commands run through this plugin) nothing accumulates in its
    /// _logs queue and the LogCommand* helpers are never reached either. Hooking
    /// Application.logMessageReceived ourselves is independent of console state, so
    /// anything a command writes via Debug.Log is seen regardless.
    /// </summary>
    public static void InstallUnityHook()
    {
        if (UnityHookInstalled) return;
        try
        {
            _unityHook = DelegateSupport.ConvertDelegate<Application.LogCallback>(
                new Action<string, string, LogType>(OnUnityLog));
            Application.add_logMessageReceived(_unityHook);
            UnityHookInstalled = true;
        }
        catch (Exception)
        {
            UnityHookInstalled = false; // reported through console.exec's captureSource
        }
    }

    public static void RemoveUnityHook()
    {
        if (!UnityHookInstalled || _unityHook == null) return;
        try { Application.remove_logMessageReceived(_unityHook); } catch { }
        UnityHookInstalled = false;
        _unityHook = null;
    }

    private static void OnUnityLog(string condition, string stackTrace, LogType type)
    {
        if (string.IsNullOrWhiteSpace(condition)) return;
        var prefix = type is LogType.Error or LogType.Exception or LogType.Assert ? "[error] "
            : type == LogType.Warning ? "[warn] " : "";
        Add(prefix + condition);
    }

    public static void Begin()
    {
        lock (_lock)
        {
            _patchBuffer.Clear();
            _capturing = true;
            _logCountAtStart = CurrentLogCount();
        }
    }

    /// <summary>Returns whatever the console printed since Begin(), best effort.</summary>
    public static List<string> End()
    {
        lock (_lock)
        {
            _capturing = false;

            var fromQueue = NewQueueEntriesSince(_logCountAtStart);

            // Merge, preserving order and dropping duplicates: a line can arrive via
            // both routes when a command uses one of the patched helpers.
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var merged = new List<string>();
            foreach (var line in fromQueue.Concat(_patchBuffer))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (seen.Add(line)) merged.Add(line);
            }
            return merged;
        }
    }

    public static void Add(string? message)
    {
        if (string.IsNullOrEmpty(message)) return;
        lock (_lock)
        {
            if (!_capturing) return;
            if (_patchBuffer.Count < 500) _patchBuffer.Add(message!);
        }
    }

    private static int CurrentLogCount()
    {
        try { return DebugConsole.Instance?._logs?.Count ?? 0; }
        catch { return 0; }
    }

    private static List<string> NewQueueEntriesSince(int startCount)
    {
        var result = new List<string>();
        try
        {
            var logs = DebugConsole.Instance?._logs;
            if (logs == null) return result;

            var all = new List<string>();
            foreach (var entry in logs)
            {
                string? text = null;
                try { text = entry?.content?.text; } catch { }
                all.Add(text ?? "");
            }

            // The queue evicts old entries once it hits _maxLogs, so if it shrank or
            // stayed level we can only report what is demonstrably new.
            var skip = Math.Min(startCount, all.Count);
            for (var i = skip; i < all.Count && result.Count < 500; i++)
            {
                if (!string.IsNullOrWhiteSpace(all[i])) result.Add(all[i]);
            }
        }
        catch
        {
            // queue shape changed between builds - fall back to patch buffer only
        }
        return result;
    }
}

[HarmonyPatch(typeof(DebugConsole), nameof(DebugConsole.LogCommandInfo))]
internal static class Patch_LogCommandInfo
{
    private static void Postfix(string message) => ConsoleOutputCapture.Add(message);
}

[HarmonyPatch(typeof(DebugConsole), nameof(DebugConsole.LogCommandRunning))]
internal static class Patch_LogCommandRunning
{
    private static void Postfix(string message) => ConsoleOutputCapture.Add(message);
}

[HarmonyPatch(typeof(DebugConsole), nameof(DebugConsole.LogCommandFailed))]
internal static class Patch_LogCommandFailed
{
    private static void Postfix(string message) => ConsoleOutputCapture.Add("[failed] " + message);
}

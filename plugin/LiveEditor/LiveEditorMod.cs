using System.Text.Json.Nodes;
using LiveEditor.Commands;
using LiveEditor.Core;
using LiveEditor.Net;
using RedLoader.Utils;
using Sons.Items.Core;
using SonsSdk;

namespace LiveEditor;

public class LiveEditorMod : SonsMod
{
    private const int DefaultPort = 8271;

    private MainThreadDispatcher? _dispatcher;
    private NdjsonListener? _listener;
    private string? _portFilePath;

    protected override void OnSdkInitialized()
    {
        Log("LiveEditor: SDK initialized");

        // ItemDatabaseManager's lazy init costs ~2s (VERIFIED — matches ItemSpawner's
        // own log line for the same call); force it now so DbReady is accurate from
        // the moment the listener comes up, rather than depending on some other
        // mod (e.g. ItemSpawner) happening to trigger it first.
        ItemDatabaseManager.Initialize();
        GameStateTracker.MarkDbReady();

        // Hook Unity's log stream so console command output can be captured even
        // with the in-game console closed (the console only feeds its own log buffer
        // while it is open, which is why earlier capture attempts saw nothing).
        ConsoleOutputCapture.InstallUnityHook();
        Log($"LiveEditor: unity log hook installed={ConsoleOutputCapture.UnityHookInstalled}");

        var registry = new CommandRegistry();
        registry.Register(new PingCommand());
        registry.Register(new StateGetCommand());
        registry.Register(new DescribeCommand(registry));
        registry.Register(new ItemListCommand());
        registry.Register(new ItemResolveCommand());
        registry.Register(new ItemCountCommand());
        registry.Register(new ItemAddCommand());
        registry.Register(new ItemRemoveCommand());
        registry.Register(new ItemPlateCommand());
        registry.Register(new ItemModulesCommand());
        registry.Register(new ItemSpawnCommand());
        registry.Register(new SpawnUnlockCommand());
        registry.Register(new SpawnRestoreCommand());
        registry.Register(new ReconPrefabCommand());
        registry.Register(new ConsoleListCommand());
        registry.Register(new ConsoleExecCommand());
        registry.Register(new ConsoleIntrospectCommand());
        registry.Register(new ConsoleUnlockCommand());
        registry.Register(new RestoredBuildHackCommand());
        registry.Register(new RestoredInstantBuildCommand());
        registry.Register(new DevLoadSaveCommand());
        registry.Register(new DumpItemsCommand());
        registry.Register(new PlayerGetCommand());
        registry.Register(new PlayerSetCommand());
        registry.Register(new PlayerRestoreCommand());
        registry.Register(new CheatsForceCommand());

        _dispatcher = new MainThreadDispatcher();
        _dispatcher.Start();

        var token = PortFile.NewToken();
        _listener = new NdjsonListener(registry, _dispatcher, token);
        _listener.Start(DefaultPort);

        // If the host flips "Allow Cheats" mid-session the game pushes the new state
        // down and would close the console again; re-assert on every change, and tell
        // connected clients so their UI can reflect it.
        try
        {
            SdkEvents.OnCheatsEnabledChanged.Subscribe(enabled =>
            {
                CheatGateBypass.Apply();
                _listener?.Broadcast("session.changed", new JsonObject
                {
                    ["mode"] = SessionState.ModeString,
                    ["cheatsEnabled"] = enabled,
                });
            });
        }
        catch (Exception ex)
        {
            Log($"LiveEditor: could not subscribe to OnCheatsEnabledChanged ({ex.Message})");
        }

        // world.exited was declared in the protocol and handled by the app, but never
        // actually broadcast — so leaving a world left clients showing stale state.
        try
        {
            SdkEvents.OnWorldExited.Subscribe(() =>
            {
                ConsoleCatalog.Invalidate();
                _listener?.Broadcast("world.exited");
            });
        }
        catch (Exception ex)
        {
            Log($"LiveEditor: could not subscribe to OnWorldExited ({ex.Message})");
        }

        _portFilePath = Path.Combine(LoaderEnvironment.UserDataDirectory, "LiveEditor.port");
        PortFile.Write(_portFilePath, _listener.Port, token);

        Log($"LiveEditor: listening on 127.0.0.1:{_listener.Port} (port file: {_portFilePath})");
    }

    /// <summary>
    /// Without this the listener threads survive teardown and, worse, LiveEditor.port
    /// is left on disk holding a dead port+token — so the external app's next launch
    /// reads stale details, fails to connect, and reports the mod as not loaded while
    /// it is in fact running on a different port.
    /// </summary>
    protected override void OnApplicationQuit()
    {
        try { ConsoleOutputCapture.RemoveUnityHook(); } catch { }
        try { _listener?.Stop(); } catch (Exception ex) { Log($"listener stop failed: {ex.Message}"); }
        try { _dispatcher?.Stop(); } catch (Exception ex) { Log($"dispatcher stop failed: {ex.Message}"); }

        try
        {
            if (_portFilePath != null && File.Exists(_portFilePath)) File.Delete(_portFilePath);
        }
        catch (Exception ex)
        {
            Log($"could not remove port file: {ex.Message}");
        }
    }

    protected override void OnGameStart()
    {
        Log("LiveEditor: game start");

        // The DebugConsole (and therefore its autocomplete list) only exists once a
        // world is loaded, so the catalog built at startup has no hidden-flag data.
        // Drop it here and it will be rebuilt with real visibility info on next use.
        ConsoleCatalog.Invalidate();

        // Re-open the console gates now that the console object exists. Patching the
        // getters is not enough on its own: RedLoader pushes the host's cheat setting
        // into SetCheatsAllowed/SetBlockConsole on join, so the stored state needs
        // overwriting too.
        CheatGateBypass.Apply();
        Log($"LiveEditor: cheat gate bypass={CheatGateBypass.Enabled} ({CheatGateBypass.DescribeState()})");

        _listener?.Broadcast("world.entered", new JsonObject
        {
            ["mode"] = SessionState.ModeString,
        });
    }
}

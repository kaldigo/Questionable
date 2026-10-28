// Authored with LLM assistance, changes must be reviewed and owned by a human.
// Initial version reviewed and owned by @kaldigo
using Dalamud.Plugin.Ipc;
using static Questionable.External.IPCUtils;

namespace Questionable.External.Reborn;

/// <summary>Reborn-specific IPC and lifecycle; upstream VBM behaviour stays in BossModIpc.</summary>
[RegisterSingleton]
internal sealed class RebornBossModBackend : IDisposable
{
    private readonly IDalamudPluginInterface _plugins;
    private readonly ICommandManager _commands;
    private readonly IFramework _framework;
    private readonly ILogger<RebornBossModBackend> _logger;
    private readonly ICallGateSubscriber<string, string?> _get;
    private readonly ICallGateSubscriber<string, bool, bool> _create;
    private readonly ICallGateSubscriber<string, bool> _delete;
    private readonly ICallGateSubscriber<string?> _active;
    private readonly ICallGateSubscriber<string, bool> _activate;
    private readonly ICallGateSubscriber<bool> _clear;
    private readonly ICallGateSubscriber<List<string>, bool, List<string>> _config;
    private readonly RebornSession _session;
    private readonly HashSet<string> _ownedPresets = [];
    private bool _started;

    public RebornBossModBackend(IDalamudPluginInterface plugins, ICommandManager commands,
        IFramework framework, ILogger<RebornBossModBackend> logger)
    {
        _plugins = plugins;
        _commands = commands;
        _framework = framework;
        _logger = logger;
        // Both variants publish this namespace; never call it when both are loaded.
        _get = plugins.GetIpcSubscriber<string, string?>("BossMod.Presets.Get");
        _create = plugins.GetIpcSubscriber<string, bool, bool>("BossMod.Presets.Create");
        _delete = plugins.GetIpcSubscriber<string, bool>("BossMod.Presets.Delete");
        _active = plugins.GetIpcSubscriber<string?>("BossMod.Presets.GetActive");
        _activate = plugins.GetIpcSubscriber<string, bool>("BossMod.Presets.SetActive");
        _clear = plugins.GetIpcSubscriber<bool>("BossMod.Presets.ClearActive");
        _config = plugins.GetIpcSubscriber<List<string>, bool, List<string>>("BossMod.Configuration");
        _session = new(ReadSetting, WriteSetting);
    }

    public bool IsLoaded => _plugins.InstalledPlugins.Any(p => p.InternalName == "BossModReborn" && p.IsLoaded);
    public bool HasConflict => IsLoaded && IPCSubscriber.IsInstalled("BossMod");
    public bool HasSession => _started || _session.HasSettings;
    public Version? Version => _plugins.InstalledPlugins
        .FirstOrDefault(p => p.InternalName == "BossModReborn" && p.IsLoaded)?.Version;
    public bool IsReady => IpcInvoke.SafeFunc(() => IsLoaded && !HasConflict
        && _commands.Commands.ContainsKey("/bmrai") && _get.HasFunction && _create.HasFunction
        && _delete.HasFunction && _active.HasFunction && _activate.HasFunction && _clear.HasFunction
        && _config.HasFunction, fallback: false);

    private void RequireReady()
    {
        if (!IsReady)
            throw new InvalidOperationException("BossMod Reborn is unavailable or both BossMod variants are loaded.");
    }

    public void EnsurePreset(BossModIpc.PresetDefinition definition)
    {
        RequireReady();
        string content = RebornSession.PreparePreset(definition.Content);
        string name = RebornSession.PresetName(definition.Name);
        if (!_create.InvokeFunc(content, true))
            throw new InvalidOperationException($"Reborn rejected preset {name}.");
        RebornSession.ValidatePreset(content, _get.InvokeFunc(name));
        _ownedPresets.Add(name);
    }

    public bool DeletePreset(BossModIpc.PresetDefinition definition)
    {
        RequireReady();
        return _delete.InvokeFunc(RebornSession.PresetName(definition.Name));
    }

    public void Activate(BossModIpc.PresetDefinition definition, bool solo, bool restoreMovement)
    {
        RequireReady();
        try
        {
            EnsurePreset(definition);
            _started = true;
            // Stop the separate party-follow AI before the preset takes ownership of movement.
            if (!_commands.ProcessCommand("/bmrai off"))
                throw new InvalidOperationException("Could not stop Reborn's follow AI.");
            if (solo)
            {
                _session.Override("AutorotationConfig", "ClearPresetOnCombatEnd", false);
                _session.Override("ZoneModuleConfig", "EnableQuestBattles", true);
                if (restoreMovement)
                    _session.Override("ActionTweaksConfig", "PreventMovingWhileCasting", false);
            }
            string name = RebornSession.PresetName(definition.Name);
            if (!_activate.InvokeFunc(name) || _active.InvokeFunc() != name)
                throw new InvalidOperationException($"Reborn did not activate preset {name}.");
        }
        catch
        {
            Cleanup();
            throw;
        }
    }

    public void ClearActivePresets()
    {
        RequireReady();
        string? active = _active.InvokeFunc();
        // Recognise integration presets after a plugin reload, without clearing a user's preset.
        if (active is "Questionable [Reborn]" or "Questionable - Quest Battles [Reborn]"
            or "Questionable - Normal Movement [Reborn]" || (active != null && _ownedPresets.Contains(active)))
        {
            if (!_clear.InvokeFunc() || _active.InvokeFunc() == active)
                throw new InvalidOperationException("Reborn did not clear the Questionable preset.");
        }
    }

    public void Cleanup()
    {
        if (!IsReady)
        {
            if (HasSession)
                throw new InvalidOperationException("Cannot clean up Reborn until it is loaded without another BossMod variant.");
            return;
        }
        List<Exception> failures = [];
        // Each cleanup action runs even when another one fails.
        try { if (_started && !_commands.ProcessCommand("/bmrai off")) throw new InvalidOperationException("Could not stop Reborn AI."); }
        catch (Exception e) { failures.Add(e); }
        try { ClearActivePresets(); }
        catch (Exception e) { failures.Add(e); }
        try { _session.Restore(); }
        catch (Exception e) { failures.Add(e); }
        if (failures.Count != 0)
            throw new AggregateException("Reborn cleanup failed; stop questing and inspect Reborn.", failures);
        _started = false;
    }

    private bool ReadSetting(string node, string field)
    {
        RequireReady();
        List<string> result = _config.InvokeFunc([node, field], false);
        if (result.Count != 1 || !bool.TryParse(result[0], out bool value))
            throw new InvalidOperationException($"Could not read Reborn {node}.{field}.");
        return value;
    }

    private void WriteSetting(string node, string field, bool value)
    {
        RequireReady();
        // save=false changes only the live value; cleanup restores it and verifies the result.
        List<string> result = _config.InvokeFunc([node, field, value ? "true" : "false"], false);
        if (result.Count != 0)
            throw new InvalidOperationException(string.Join("; ", result));
    }

    public void Dispose() => IpcInvoke.TryOnFrameworkThread(_framework, Cleanup, _logger);
}

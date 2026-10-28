using System.Collections.ObjectModel;
using Dalamud.Plugin.Ipc;
using Questionable.Model.Questing;
using Questionable.External.Reborn;
using static Questionable.External.IPCUtils;
namespace Questionable.External;

[RegisterSingleton]
internal sealed class BossModIpc
(
    IDalamudPluginInterface pluginInterface,
    Configuration configuration,
    TerritoryData territoryData,
    ICommandManager commandManager,
    RebornBossModBackend reborn) : Ipc
{
    public override string InternalName => reborn.IsLoaded ? "BossModReborn" : PluginName;
    public override Version? GetVersion() => reborn.IsLoaded ? reborn.Version : IPCSubscriber.Version(InternalName);
    public override bool IsReady() => reborn.IsLoaded ? reborn.IsReady : IpcInvoke.SafeFunc(() =>
        {
            var _ = _getActivePreset.InvokeFunc();
            return GetVersion() != null;
        }, fallback: false);
    public enum EPreset
    {
        Overworld,
        QuestBattle,
        NormalMovement
    }

    private const string PluginName = "BossMod";

    private static readonly ReadOnlyDictionary<EPreset, PresetDefinition> PresetDefinitions = new Dictionary<EPreset, PresetDefinition>
    {
        { EPreset.Overworld, new("Questionable", "Overworld") },
        { EPreset.QuestBattle, new("Questionable - Quest Battles", "QuestBattle") },
        { EPreset.NormalMovement, new("Questionable - Normal Movement", "NormalMovement") }
    }.AsReadOnly();
    private readonly ICallGateSubscriber<bool> _clearPreset = pluginInterface.GetIpcSubscriber<bool>($"{PluginName}.Presets.ClearActive");

    private readonly ICallGateSubscriber<string, bool, bool> _createPreset = pluginInterface.GetIpcSubscriber<string, bool, bool>($"{PluginName}.Presets.Create");
    private readonly ICallGateSubscriber<string?> _getActivePreset = pluginInterface.GetIpcSubscriber<string?>($"{PluginName}.Presets.GetActive");
    private readonly ICallGateSubscriber<string, string?> _getPreset = pluginInterface.GetIpcSubscriber<string, string?>($"{PluginName}.Presets.Get");
    private readonly ICallGateSubscriber<string, bool> _setPreset = pluginInterface.GetIpcSubscriber<string, bool>($"{PluginName}.Presets.SetActive");
    private readonly ICallGateSubscriber<string, bool> _deletePreset = pluginInterface.GetIpcSubscriber<string, bool>($"{PluginName}.Presets.Delete");
    private readonly ICallGateSubscriber<IReadOnlyList<string>, bool, IReadOnlyList<string>> _configuration =
        pluginInterface.GetIpcSubscriber<IReadOnlyList<string>, bool, IReadOnlyList<string>>($"{PluginName}.Configuration");

    private bool _soloDutyZoneConfigured;
    private bool _enableQuestBattlesOverridden;
    public bool IsSupported() => reborn.IsLoaded ? reborn.IsReady : IpcInvoke.SafeFunc(() => _getPreset.HasFunction, fallback: false);
    public bool HasBossModConflict() => reborn.HasConflict;
    public bool CanClearObstacleMaps => !reborn.IsLoaded && commandManager.Commands.ContainsKey("/vbm");

    public PresetDefinition AddPreset(EPreset preset) => AddPreset(PresetDefinitions[preset]);
    public PresetDefinition AddPreset(PresetDefinition definition, bool bulk = false)
    {
        if (reborn.IsLoaded)
        {
            reborn.EnsurePreset(definition);
            return definition;
        }
        if (!bulk)
            ClearObstacleMaps();
        if (_getPreset.InvokeFunc(definition.Name) == null)
            _createPreset.InvokeFunc(definition.Content, true);
        return definition;
    }

    public void AddAllPresets(bool delete = false)
    {
        if (reborn.IsLoaded)
        {
            foreach (PresetDefinition definition in PresetDefinitions.Values)
                reborn.EnsurePreset(definition);
            return;
        }
        ClearObstacleMaps();
        foreach (PresetDefinition definition in PresetDefinitions.Values)
        {
            if (delete)
                DeletePreset(definition);
            AddPreset(definition, bulk: true);
        }
    }

    public bool DeletePreset(EPreset preset) => DeletePreset(PresetDefinitions[preset]);

    public bool DeletePreset(PresetDefinition definition) => reborn.IsLoaded
        ? reborn.DeletePreset(definition) : _deletePreset.InvokeFunc(definition.Name);

    public void SetActivePreset(EPreset preset)
    {
        if (reborn.IsLoaded)
        {
            reborn.Activate(PresetDefinitions[preset], solo: false, restoreMovement: false);
            return;
        }
        PresetDefinition definition = AddPreset(preset);
        commandManager.ProcessCommand("/vbmai off");
        _setPreset.InvokeFunc(definition.Name);
    }

    public void SetPresetForSoloDuty(EPreset preset)
    {
        if (reborn.IsLoaded)
        {
            reborn.Activate(PresetDefinitions[preset], solo: true, configuration.Advanced.BMRestoreMovement);
            return;
        }
        UnsetBasicSettings();
        ConfigureZoneForQuestBattle();
        SetActivePreset(preset);
    }

    // i guess we're doing plugin subtweeting now
    public void UnsetBasicSettings()
    {
        // Reborn owns and restores its temporary setting changes as part of session cleanup.
        if (reborn.IsLoaded)
            return;
        if (configuration.Advanced.BMRestoreMovement)
            _configuration.InvokeFunc(["ActionTweaksConfig", "PreventMovingWhileCasting", "false"], false);
    }

    public void LoadPresets()
    {
        ClearActivePresets();
    }

    public void ClearActivePresets()
    {
        if (reborn.IsLoaded)
        {
            reborn.ClearActivePresets();
            return;
        }
        string? activePreset = _getActivePreset.InvokeFunc();
        if (activePreset == null)
            return;

        foreach (PresetDefinition definition in PresetDefinitions.Values)
        {
            if (definition.Name.Equals(activePreset, StringComparison.Ordinal))
            {
                _clearPreset.InvokeFunc();
                return;
            }
        }
    }

    public void DisableSoloDutyPreset()
    {
        if (reborn.IsLoaded || reborn.HasSession)
        {
            reborn.Cleanup();
            return;
        }
        ReleaseSoloDutyZone();
        ClearActivePresets();
    }

    public void Cleanup()
    {
        if (reborn.IsLoaded || reborn.HasSession)
        {
            reborn.Cleanup();
            return;
        }
        commandManager.ProcessCommand("/vbmai off");
        ReleaseSoloDutyZone();
        ClearActivePresets();
    }

    public void ClearObstacleMaps()
    {
        if (CanClearObstacleMaps)
            commandManager.ProcessCommand("/vbm clear-maps");
    }

    private void ConfigureZoneForQuestBattle()
    {
        if (!_soloDutyZoneConfigured)
        {
            commandManager.ProcessCommand("/vbm cfg Autorotation ClearPresetOnCombatEnd false");
            _soloDutyZoneConfigured = true;
        }

        bool? enableQuestBattles = TryGetEnableQuestBattles();
        if (enableQuestBattles is not true)
            SetEnableQuestBattles(enabled: true);
        _enableQuestBattlesOverridden = enableQuestBattles == false;
    }

    private void ReleaseSoloDutyZone()
    {
        if (_enableQuestBattlesOverridden)
        {
            SetEnableQuestBattles(enabled: false);
            _enableQuestBattlesOverridden = false;
        }

        _soloDutyZoneConfigured = false;
    }

    private bool? TryGetEnableQuestBattles()
    {
        if (!_configuration.HasFunction)
            return null;

        return IpcInvoke.SafeFunc(() =>
        {
            IReadOnlyList<string> result = _configuration.InvokeFunc(
                ["cfg", "ZoneModuleConfig", "EnableQuestBattles"], false);
            if (result.Count == 0)
                return (bool?)null;

            return bool.TryParse(result[0], out bool value) ? value : null;
        }, fallback: null);
    }

    private void SetEnableQuestBattles(bool enabled) =>
        commandManager.ProcessCommand(enabled
            ? "/vbm cfg ZoneModuleConfig EnableQuestBattles true"
            : "/vbm cfg ZoneModuleConfig EnableQuestBattles false");

    public bool IsConfiguredToRunSoloInstance(ElementId questId, SinglePlayerDutyOptions? dutyOptions)
    {
        if (!IsSupported())
            return false;

        if (!configuration.SinglePlayerDuties.RunSoloInstancesWithBossMod)
            return false;

        dutyOptions ??= new();
        if (!territoryData.TryGetContentFinderConditionForSoloInstance(questId, dutyOptions.Index, out TerritoryData.ContentFinderConditionData? cfcData))
            return false;

        if (configuration.SinglePlayerDuties.BlacklistedSinglePlayerDutyCfcIds.Contains(cfcData.ContentFinderConditionId))
            return false;

        if (configuration.SinglePlayerDuties.WhitelistedSinglePlayerDutyCfcIds.Contains(cfcData.ContentFinderConditionId))
            return true;

        return dutyOptions.Enabled;
    }

    public sealed class PresetDefinition(string name, string fileName)
    {
        public string Name { get; } = name;
        public string Content { get; } = LoadPreset(fileName);

        public static string LoadPreset(string name)
        {
            Stream stream =
                typeof(BossModIpc).Assembly.GetManifestResourceStream(
                    $"Questionable.Controller.CombatModules.BossModPreset.{name}") ??
                throw new InvalidOperationException(_LF("Preset {0} was not found", name));
            using StreamReader reader = new(stream);
            return reader.ReadToEnd();
        }
    }
}

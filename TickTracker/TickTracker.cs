using System;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.Concurrent;

using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using FFXIVClientStructs.FFXIV.Client.Graphics.Kernel;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Hooking;
using Dalamud.Utility;
using Dalamud.Utility.Signatures;
using Dalamud.Interface.Windowing;
using Dalamud.Game.Config;
using Dalamud.Game.Command;
using Dalamud.Game.NativeWrapper;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.JobGauge.Types;
using Dalamud.Game.ClientState.Objects.SubKinds;
using TickTracker.NativeNodes;
using TickTracker.Windows;
using TickTracker.Helpers;
using TickTracker.IPC;

namespace TickTracker;

public sealed class TickTracker : IDalamudPlugin
{
    /// <summary>
    /// A <see cref="FrozenSet{T}"/> of addons to fetch for collision checks.
    /// </summary>
    private readonly FrozenSet<string> addonsLookup = Utilities.CreateFrozenSet("Talk", "ActionDetail", "ItemDetail", "Inventory", "Character");
    /// <summary>
    /// A <see cref="FrozenSet{T}" /> of Status IDs that trigger HP regen
    /// </summary>
    private FrozenSet<uint> healthRegenSet = [];
    /// <summary>
    /// A <see cref="FrozenSet{T}" /> of Status IDs that trigger MP regen
    /// </summary>
    private FrozenSet<uint> manaRegenSet = [];
    /// <summary>
    /// A <see cref="FrozenSet{T}" /> of Status IDs that stop HP regen
    /// </summary>
    private FrozenSet<uint> disabledHealthRegenSet = [];

    private readonly FrozenSet<string> physicalDPSAbbreviations = Utilities.CreateFrozenSet(
        "PGL", "LNC", "ARC", "MNK", "DRG", "BRD", "ROG", "NIN", "MCH", "SAM", "DNC", "RPR", "VPR");
    private readonly FrozenSet<string> discipleOfTheLandAbbreviations = Utilities.CreateFrozenSet("MIN", "BTN", "FSH");
    private FrozenSet<uint> physicalDPS = [];
    private FrozenSet<uint> discipleOfTheLand = [];

    // Function that triggers when client receives a network packet with an update for nearby actors
    private unsafe delegate void ReceiveActorUpdateDelegate(uint objectId, uint* packetData, byte unkByte);

    [Signature("48 89 5C 24 ?? 48 89 6C 24 ?? 56 48 83 EC 20 83 3D ?? ?? ?? ?? ?? 41 0F B6 E8 48 8B DA 8B F1 0F 84 ?? ?? ?? ?? 48 89 7C 24 ??", DetourName = nameof(ActorTickUpdate))]
    private readonly Hook<ReceiveActorUpdateDelegate>? receiveActorUpdateHook = null;

    private readonly IDalamudPluginInterface pluginInterface;
    private readonly Configuration config;
    private readonly Utilities utilities;
    private readonly IClientState clientState;
    private readonly IFramework framework;
    private readonly IGameGui gameGui;
    private readonly ICommandManager commandManager;
    private readonly ICondition condition;
    private readonly IPluginLog log;
    private readonly IGameConfig gameConfig;
    private readonly IAddonLifecycle addonLifecycle;
    private readonly IObjectTable objectTable;

    private ConfigWindow ConfigWindow { get; set; } = null!;
    private DebugWindow DebugWindow { get; set; } = null!;
    private HPBar HPBarWindow { get; set; } = null!;
    private MPBar MPBarWindow { get; set; } = null!;
    private GPBar GPBarWindow { get; set; } = null!;
#if DEBUG
    public static DevWindow DevWindow { get; set; } = null!;
#endif
    public static Vector2 Resolution { get; private set; }

    public WindowSystem WindowSystem { get; } = new("TickTracker");
    private const string CommandName = "/tick";
    private const float RegularTickInterval = 3, FastTickInterval = 1.5f;
    private const string ParamWidgetUldPath = "ui/uld/parameter.uld";

    private readonly FrozenSet<BarWindowBase> barWindows;
    private readonly uint primaryTickerImageID = NativeUi.Get("TickerImagePrimary");
    private readonly uint secondaryTickerImageID = NativeUi.Get("TickerImageSecondary");
    private readonly ImageNode primaryTickerNode, secondaryTickerNode;
    private readonly CancellationTokenSource cts;
    private readonly BLMGauge blmGauge;

    private double syncValue, regenValue, fastValue;
    private bool finishedLoading, primaryNodeCreationFailed, secondaryNodeCreationFailed, penumbraAvailable;
    private bool syncAvailable = true, nullSheet = true;
    private uint lastHPValue, lastMPValue, lastGPValue;
    private Task? loadingTask;
    private PenumbraApi? penumbraApi;
    private unsafe AtkUnitBasePtr NameplateAddon => gameGui.GetAddonByName("NamePlate");
    private unsafe AddonParameterWidget* ParamWidget => (AddonParameterWidget*)gameGui.GetAddonByName("_ParameterWidget").Address;


    public TickTracker(IDalamudPluginInterface _pluginInterface,
        IClientState _clientState,
        IFramework _framework,
        IGameGui _gameGui,
        ICommandManager _commandManager,
        ICondition _condition,
        IDataManager _dataManager,
        IJobGauges _jobGauges,
        IPluginLog _pluginLog,
        IGameConfig _gameConfig,
        IAddonLifecycle _addonLifecycle,
        IObjectTable _objectTable,
        IGameInteropProvider _interopProvider)
    {
        pluginInterface = _pluginInterface;
        clientState = _clientState;
        framework = _framework;
        gameGui = _gameGui;
        commandManager = _commandManager;
        condition = _condition;
        log = _pluginLog;
        gameConfig = _gameConfig;
        objectTable = _objectTable;
        addonLifecycle = _addonLifecycle;

        _interopProvider.InitializeFromAttributes(this);

        if (receiveActorUpdateHook is null)
        {
            throw new NotSupportedException("Hook not found in current game version. The plugin is non functional.");
        }
        receiveActorUpdateHook.Enable();

        cts = new CancellationTokenSource();
        blmGauge = _jobGauges.Get<BLMGauge>();

        var tickerUld = pluginInterface.UiBuilder.LoadUld(ParamWidgetUldPath);
        primaryTickerNode = new ImageNode(_dataManager, log, tickerUld)
        {
            NodeId = primaryTickerImageID,
        };
        secondaryTickerNode = new ImageNode(_dataManager, log, tickerUld)
        {
            NodeId = secondaryTickerImageID,
        };
        tickerUld.Dispose();

        config = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        utilities = new Utilities(pluginInterface, config, condition, _dataManager, clientState, log);
#if DEBUG
        DevWindow = new DevWindow(pluginInterface, _dataManager, log, gameGui, utilities);
        WindowSystem.AddWindow(DevWindow);
#endif
        PenumbraCheck();
        InitializeWindows();

        barWindows = WindowSystem.Windows.OfType<BarWindowBase>().Any()
            ? WindowSystem.Windows.OfType<BarWindowBase>().ToFrozenSet()
            : [];

        RegisterEvents();
        _ = Task.Run(InitializeLuminaSheets, cts.Token);
        InitializeResolution();
    }

    private void PenumbraCheck()
    {
        try
        {
            var plo = pluginInterface.InstalledPlugins.SingleOrDefault(gon => gon.InternalName.Equals("Penumbra", StringComparison.Ordinal));
            if (plo is null)
            {
                return;
            }
            if (plo.IsLoaded)
            {
                penumbraApi = new PenumbraApi(pluginInterface, log);
                return;
            }
            _ = Task.Run(async () =>
            {
                penumbraAvailable = await utilities.CheckIPC(TimeSpan.FromSeconds(30).TotalMilliseconds,
                    () => new Penumbra.Api.IpcSubscribers.GetEnabledState(pluginInterface).Invoke(),
                    cts.Token).ConfigureAwait(false);
                if (penumbraAvailable)
                {
                    penumbraApi = new PenumbraApi(pluginInterface, log);
                }
            }, cts.Token);
        }
        catch (Exception ex)
        {
            if (ex.Message.Contains("Penumbra API out of date", StringComparison.OrdinalIgnoreCase))
            {
                log.Error(ex.Message);
                return;
            }

            if (ex is InvalidOperationException)
            {
                log.Error("Too many plogons for safe consumption. {ex}", ex.Message);
            }
            else
            {
                log.Error("Penumbra IPC failed. {ex}", ex.Message);
            }
        }
    }

    private void InitializeWindows()
    {
        DebugWindow = new DebugWindow(pluginInterface);
        ConfigWindow = new ConfigWindow(pluginInterface, config, DebugWindow, penumbraApi);
        HPBarWindow = new HPBar(clientState, log, utilities, config);
        MPBarWindow = new MPBar(clientState, log, utilities, config);
        GPBarWindow = new GPBar(clientState, log, utilities, config);
        WindowSystem.AddWindow(ConfigWindow);
        WindowSystem.AddWindow(HPBarWindow);
        WindowSystem.AddWindow(MPBarWindow);
        WindowSystem.AddWindow(GPBarWindow);
        WindowSystem.AddWindow(DebugWindow);
    }

    private void RegisterEvents()
    {
        commandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open or close Tick Tracker's config window.",
        });

        pluginInterface.UiBuilder.Draw += WindowSystem.Draw;
        pluginInterface.UiBuilder.OpenConfigUi += ConfigWindow.Toggle;
        framework.Update += OnFrameworkUpdate;
        clientState.TerritoryChanged += TerritoryChanged;
        gameConfig.SystemChanged += CheckResolutionChange;
        addonLifecycle.RegisterListener(AddonEvent.PostUpdate, addonsLookup, CheckBarCollision);
        addonLifecycle.RegisterListener(AddonEvent.PreFinalize, "_ParameterWidget", NativeUiDisposeListener);
    }

    /// <summary>
    ///     Grab the resolution from the <see cref="Device.SwapChain"/> on plugin init, and propagate it as the <see cref="Window.WindowSizeConstraints.MaximumSize"/>
    ///     to the appropriate windows.
    /// </summary>
    private unsafe void InitializeResolution()
    {
        Resolution = new Vector2(Device.Instance()->SwapChain->Width, Device.Instance()->SwapChain->Height);
        Utilities.ChangeWindowConstraints(DebugWindow, Resolution);
#if DEBUG
        Utilities.ChangeWindowConstraints(DevWindow, Resolution);
#endif
    }

    private void OnFrameworkUpdate(IFramework _framework)
    {
#if DEBUG
        DevWindowThings(clientState?.LocalPlayer, (DateTime.UtcNow - DateTime.UnixEpoch).TotalSeconds, barWindows);
#endif
        if (clientState is { IsLoggedIn: false } or { IsPvPExcludingDen: true } || nullSheet)
        {
            return;
        }
        if (objectTable is not { LocalPlayer: { } player })
        {
            return;
        }
        unsafe
        {
            if (utilities.InCutscene() || player.IsDead)
            {
                HPBarWindow.IsOpen = MPBarWindow.IsOpen = GPBarWindow.IsOpen = false;
                primaryTickerNode.HideNode();
                secondaryTickerNode.HideNode();
                return;
            }
            if (!NameplateAddon.IsReady || !NameplateAddon.IsVisible)
            {
                HPBarWindow.IsOpen = MPBarWindow.IsOpen = GPBarWindow.IsOpen = false;
                primaryTickerNode.HideNode();
                secondaryTickerNode.HideNode();
                return;
            }
        }
        var now = (DateTime.UtcNow - DateTime.UnixEpoch).TotalSeconds;
        if (syncAvailable)
        {
            syncValue = now;
            regenValue = fastValue = syncValue - FastTickInterval;
            syncAvailable = false;
        }
        else if (syncValue + RegularTickInterval <= now)
        {
            syncValue += RegularTickInterval;
        }
        if (fastValue + FastTickInterval <= now)
        {
            fastValue += FastTickInterval;
        }
        if (regenValue + RegularTickInterval <= now)
        {
            regenValue += RegularTickInterval;
        }
        if (loadingTask is { IsCompleted: true })
        {
            finishedLoading = true;
            loadingTask = null;
        }
        ProcessTicks(now, player);
        UpdateBarState(player);
    }

    private void ProcessTicks(double currentTime, IPlayerCharacter player)
    {
        var statusList = player.StatusList.ToList();
        // HP section
        HPBarWindow.RegenActive = statusList.Exists(e => healthRegenSet.Contains(e.StatusId));
        HPBarWindow.TickHalted = statusList.Exists(e => disabledHealthRegenSet.Contains(e.StatusId));
        var currentHP = player.CurrentHp;
        var fullHP = currentHP == player.MaxHp;

        // MP Section
        MPBarWindow.RegenActive = statusList.Exists(e => manaRegenSet.Contains(e.StatusId));
        MPBarWindow.TickHalted = player.ClassJob.RowId is 25 && blmGauge.InAstralFire;
        var currentMP = player.CurrentMp;
        var fullMP = currentMP == player.MaxMp;

        // GP Section
        GPBarWindow.TickHalted = condition[ConditionFlag.Gathering] && (player.ClassJob.RowId is not 18 || condition[ConditionFlag.Diving]);
        var currentGP = player.CurrentGp;
        var fullGP = currentGP == player.MaxGp;

        UpdateTick(HPBarWindow, currentTime, currentHP, fullHP, lastHPValue);
        UpdateTick(MPBarWindow, currentTime, currentMP, fullMP, lastMPValue);
        UpdateTick(GPBarWindow, currentTime, currentGP, fullGP, lastGPValue);

        lastHPValue = currentHP;
        lastMPValue = currentMP;
        lastGPValue = currentGP;
    }

    private void UpdateTick(BarWindowBase window, double currentTime, uint currentResource, bool fullResource, uint lastResource)
    {
        window.PreviousProgress = window.Progress;
        if (window.TickHalted)
        {
            window.Tick = currentTime;
            window.Progress = window.PreviousProgress;
            return;
        }
        if (window.RegenActive)
        {
            window.Tick = fullResource ? regenValue : fastValue;
            window.FastRegen = !fullResource;
            window.Progress = (currentTime - window.Tick) / (fullResource ? RegularTickInterval : FastTickInterval);
            return;
        }
        if (fullResource || finishedLoading)
        {
            window.Tick = syncValue;
        }
        else if (lastResource != currentResource && window.TickUpdate)
        {
            window.Tick = currentTime;
            window.TickUpdate = false;
        }
        window.Progress = (currentTime - window.Tick) / RegularTickInterval;
    }

    private void InitializeLuminaSheets()
    {
        var statusSheet = utilities.GetSheet<Lumina.Excel.Sheets.Status>();
        var jobSheet = utilities.GetSheet<Lumina.Excel.Sheets.ClassJob>();
        if (statusSheet is null || jobSheet is null)
        {
            return;
        }
        var bag1 = new HashSet<uint>();
        var bag2 = new HashSet<uint>();
        foreach (var row in jobSheet)
        {
            var name = row.Abbreviation.GetText();
            if (physicalDPSAbbreviations.Contains(name))
            {
                bag1.Add(row.RowId);
                continue;
            }
            if (discipleOfTheLandAbbreviations.Contains(name))
            {
                bag2.Add(row.RowId);
            }
        }
        physicalDPS = bag1.ToFrozenSet();
        discipleOfTheLand = bag2.ToFrozenSet();
        var bannedStatus = Utilities.CreateFrozenSet<uint>(135, 307, 751, 1419, 1465, 1730, 2326);
        var filteredSheet = statusSheet.Where(s => !bannedStatus.Contains(s.RowId));
        ParseStatusSheet(filteredSheet, out var disabledHealthRegenBag, out var healthRegenBag, out var manaRegenBag);
        healthRegenSet = healthRegenBag.ToFrozenSet();
        manaRegenSet = manaRegenBag.ToFrozenSet();
        disabledHealthRegenSet = disabledHealthRegenBag.ToFrozenSet();
        nullSheet = false;
        log.Debug("HP regen list generated with {HPcount} status effects.", healthRegenSet.Count);
        log.Debug("MP regen list generated with {MPcount} status effects.", manaRegenSet.Count);
    }

    private void ParseStatusSheet(IEnumerable<Lumina.Excel.Sheets.Status> sheet, out ConcurrentBag<uint> disabledHealthRegenBag, out ConcurrentBag<uint> healthRegenBag, out ConcurrentBag<uint> manaRegenBag)
    {
        var bag1 = new ConcurrentBag<uint>();
        var bag2 = new ConcurrentBag<uint>();
        var bag3 = new ConcurrentBag<uint>();
        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = Environment.ProcessorCount / 2, // Surely no one will cause an access issue by using an 128 core CPU hahaha
        };
        Parallel.ForEach(sheet, parallelOptions, stat =>
        {
            var text = stat.Description.GetText();
            if (text.IsNullOrWhitespace())
            {
                return;
            }
            var statName = stat.Name.GetText();
            if (Utilities.WholeKeywordMatch(text, utilities.RegenNullKeywords) && Utilities.WholeKeywordMatch(text, utilities.HealthKeywords))
            {
                bag1.Add(stat.RowId);
                DebugWindow.DisabledHealthRegenDictionary.TryAdd(stat.RowId, statName);
            }
            if (Utilities.WholeKeywordMatch(text, utilities.RegenNullKeywords) && Utilities.WholeKeywordMatch(text, utilities.ManaKeywords))
            {
                DebugWindow.DisabledManaRegenDictionary.TryAdd(stat.RowId, statName);
            }
            if (Utilities.KeywordMatch(text, utilities.RegenKeywords) && Utilities.KeywordMatch(text, utilities.TimeKeywords))
            {
                if (Utilities.KeywordMatch(text, utilities.HealthKeywords))
                {
                    bag2.Add(stat.RowId);
                    DebugWindow.HealthRegenDictionary.TryAdd(stat.RowId, statName);
                }
                if (Utilities.KeywordMatch(text, utilities.ManaKeywords))
                {
                    bag3.Add(stat.RowId);
                    DebugWindow.ManaRegenDictionary.TryAdd(stat.RowId, statName);
                }
            }
        });
        disabledHealthRegenBag = bag1;
        healthRegenBag = bag2;
        manaRegenBag = bag3;
    }

    /// <summary>
    /// This detour function is triggered every time the client receives
    /// a network packet containing an update for the nearby actors
    /// with HP, MP, GP
    /// </summary>
    /// HP = *(int*)packetData;
    /// MP = *((ushort*)packetData + 2);
    /// GP = *((short*)packetData + 3); // Goes up to 10000 and is tracked and updated at all times
    private unsafe void ActorTickUpdate(uint entityId, uint* packetData, byte unkByte)
    {
        receiveActorUpdateHook!.Original(entityId, packetData, unkByte);
        try
        {
            if (objectTable is not { LocalPlayer: { } player })
            {
                return;
            }
            if (entityId != player.EntityId)
            {
                return;
            }

            HPBarWindow.TickUpdate = player.CurrentHp != player.MaxHp;
            MPBarWindow.TickUpdate = player.CurrentMp != player.MaxMp;
            GPBarWindow.TickUpdate = true;
            syncAvailable = true;
            finishedLoading = false;
        }
        catch (Exception e)
        {
            log.Error(e, e.Message + "\nAn error has occured with the PrimaryActorTickUpdate detour.");
        }
    }

    private unsafe void UpdateBarState(IPlayerCharacter player)
    {
        var jobId = player.ClassJob.RowId;
        var althideForPhysicalDPS = physicalDPS.Contains(jobId);
        var isDiscipleOfTheLand = discipleOfTheLand.Contains(jobId);
        var Enemy = player.TargetObject?.ObjectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.BattleNpc;
        var inCombat = condition[ConditionFlag.InCombat];
        var inDuelingArea = condition[ConditionFlag.InDuelingArea];
        var hideForGPBar = isDiscipleOfTheLand && config.GPVisible;

        var shouldShowHPBar = ShowBar(inCombat, player.CurrentHp == player.MaxHp, Enemy) && !inDuelingArea;
        var shouldShowMPBar = ShowBar(inCombat, player.CurrentMp == player.MaxMp, Enemy) && !inDuelingArea && !althideForPhysicalDPS;
        var shouldShowGPBar = isDiscipleOfTheLand && (!config.HideOnFullResource || player.CurrentGp != player.MaxGp) && !inDuelingArea;

        HPBarWindow.IsOpen = !config.LockBar || (shouldShowHPBar && config.HPVisible);
        MPBarWindow.IsOpen = !config.LockBar || (shouldShowMPBar && config.MPVisible && !hideForGPBar);
        GPBarWindow.IsOpen = !config.LockBar || (shouldShowGPBar && config.GPVisible);
        if (penumbraAvailable && penumbraApi is not null && penumbraApi.NativeUiBanned)
        {
            if (primaryTickerNode.imageNode is not null)
            {
                primaryTickerNode.DestroyNode();
            }
            if (secondaryTickerNode.imageNode is not null)
            {
                secondaryTickerNode.DestroyNode();
            }
            return;
        }
        if (utilities.IsAddonReady(ParamWidget) && ParamWidget->UldManager.LoadedState is AtkLoadState.Loaded && ParamWidget->IsVisible)
        {
            DrawNativePrimary(shouldShowHPBar);
            DrawNativeSecondary(shouldShowMPBar && !isDiscipleOfTheLand, shouldShowGPBar);
        }
    }

    private bool ShowBar(bool inCombat, bool fullResource, bool Enemy)
    {
        var showBar = true;
        if (inCombat)
        {
            if (config.HideOnFullResource && fullResource)
            {
                showBar = config.AlwaysShowInCombat
                    || config.ShowOnlyInCombat
                    || (config.AlwaysShowInDuties && utilities.InDuty())
                    || (config.AlwaysShowWithHostileTarget && Enemy);
            }
            if (config.ShowOnlyInCombat)
            {
                if (config.AlwaysShowInDuties && utilities.InDuty())
                {
                    showBar = true;
                }
                if (config.AlwaysShowWithHostileTarget && Enemy)
                {
                    showBar = true;
                }
            }
        }
        else
        {
            if (config.HideOnFullResource && fullResource)
            {
                showBar = (config.AlwaysShowInDuties && utilities.InDuty())
                    || (config.AlwaysShowWithHostileTarget && Enemy);
            }
            if (config.ShowOnlyInCombat)
            {
                showBar = (config.AlwaysShowInDuties && utilities.InDuty())
                    || (config.AlwaysShowWithHostileTarget && Enemy);
            }
        }
        return showBar;
    }

    private void TerritoryChanged(ushort e)
    {
        loadingTask = Task.Run(async () => await utilities.Loading(1000).ConfigureAwait(false), cts.Token);
    }

    /// <summary>
    ///     Retrieve the resolution from the <see cref="Device.SwapChain"/> on resolution or screen mode changes.
    /// </summary>
    private unsafe void CheckResolutionChange(object? sender, ConfigChangeEvent e)
    {
        var configOption = e.Option.ToString();
        if (configOption is "FullScreenWidth" or "FullScreenHeight" or "ScreenWidth" or "ScreenHeight" or "ScreenMode")
        {
            Resolution = new Vector2(Device.Instance()->SwapChain->Width, Device.Instance()->SwapChain->Height);
        }
        Utilities.ChangeWindowConstraints(DebugWindow, Resolution);
#if DEBUG
        Utilities.ChangeWindowConstraints(DevWindow, Resolution);
#endif
    }

    /// <summary>
    ///     Delegate used to hide a <see cref="BarWindowBase"/> window if there's an overlap in-between it
    ///     and the addon that triggered the function.
    /// </summary>
    private unsafe void CheckBarCollision(AddonEvent type, AddonArgs args)
    {
        if (!config.CollisionDetection || (config.DisableCollisionInCombat && condition[ConditionFlag.InCombat]) || !config.LockBar)
        {
            return;
        }
        var currentAddon = args.Addon;
        if (!currentAddon.IsVisible)
        {
            return;
        }
        var scaled = (int)currentAddon.Scale != 100;
        foreach (var barWindow in barWindows.Where(window => Utilities.AddonOverlap(currentAddon, window, scaled)))
        {
            barWindow.IsOpen = false;
        }
    }

#if DEBUG
    private unsafe void DevWindowThings(IPlayerCharacter? player, double currentTime, FrozenSet<BarWindowBase> windowList)
    {
        DevWindow.IsOpen = true;
        if (penumbraAvailable && penumbraApi is not null)
        {
            DevWindow.Print(nameof(penumbraApi.NativeUiBanned) + " is " + penumbraApi.NativeUiBanned);
        }
        if (player is null)
        {
            return;
        }
        var cultureFormat = System.Globalization.CultureInfo.InvariantCulture;
        foreach (var window in windowList)
        {
            DevWindow.Print($"{window.WindowName} open: {window.IsOpen}");
            DevWindow.Print("Current Time: " + currentTime.ToString(cultureFormat));
            DevWindow.Print("RegenActive: " + window.RegenActive.ToString());
            DevWindow.Print("Progress: " + window.Progress.ToString(cultureFormat));
            DevWindow.Print("NormalTick: " + window.Tick.ToString(cultureFormat));
            DevWindow.Print("NormalUpdate: " + window.TickUpdate.ToString());
            DevWindow.Print("Sync Value: " + syncValue.ToString(cultureFormat));
            DevWindow.Print("Regen Value: " + regenValue.ToString(cultureFormat));
            DevWindow.Print("Fast Value: " + fastValue.ToString(cultureFormat));
            DevWindow.Print("Swapchain resolution: " + Resolution.X.ToString(cultureFormat) + "x" + Resolution.Y.ToString(cultureFormat));
        }
    }
#endif

    private unsafe void DrawNativePrimary(bool hpVisible)
    {
        if (!config.HPNativeUiVisible || primaryNodeCreationFailed)
        {
            if (primaryTickerNode.imageNode is not null)
            {
                primaryTickerNode.DestroyNode();
            }
            return;
        }
        HandleNativeNode(primaryTickerNode,
            ParamWidget->HealthGaugeBar,
            hpVisible,
            HPBarWindow.Progress,
            config.HPNativeUiColor,
            ref primaryNodeCreationFailed);
    }

    private unsafe void DrawNativeSecondary(bool mpVisible, bool gpVisible)
    {
        if ((!config.MPNativeUiVisible && !config.GPNativeUiVisible) || secondaryNodeCreationFailed)
        {
            if (secondaryTickerNode.imageNode is not null)
            {
                secondaryTickerNode.DestroyNode();
            }
            return;
        }
        if (!gpVisible)
        {
            HandleNativeNode(secondaryTickerNode,
            ParamWidget->ManaGaugeBar,
            mpVisible && config.MPNativeUiVisible,
            MPBarWindow.Progress,
            config.MPNativeUiColor,
            ref secondaryNodeCreationFailed);
        }
        if (!mpVisible)
        {
            HandleNativeNode(secondaryTickerNode,
            ParamWidget->ManaGaugeBar,
            gpVisible && config.GPNativeUiVisible,
            GPBarWindow.Progress,
            config.GPNativeUiColor,
            ref secondaryNodeCreationFailed);
        }
    }

    private unsafe void HandleNativeNode(in ImageNode tickerNode, AtkComponentGaugeBar* gaugeBase, bool visibility, double progress, Vector4 Color, ref bool failed)
    {
        if (tickerNode.imageNode is not null)
        {
            if (!visibility)
            {
                if (tickerNode.imageNode->AtkResNode.Width is not 0)
                {
                    tickerNode.imageNode->AtkResNode.SetWidth(0);
                }
                if (tickerNode.imageNode->AtkResNode.IsVisible())
                {
                    tickerNode.imageNode->AtkResNode.ToggleVisibility(visibility);
                }
                return;
            }
            progress = Math.Clamp(progress, 0, 1);
            tickerNode.imageNode->AtkResNode.SetWidth(progress > 0 ? (ushort)((progress * 152) + 4) : (ushort)0);
            tickerNode.ChangeNodeColorAndAlpha(Color);
            tickerNode.imageNode->AtkResNode.ToggleVisibility(visibility);
            return;
        }
        var frameNode = NativeUi.AttemptRetrieveNativeNode(
            gaugeBase->UldManager,
            NodeType.Image,
            (AtkImageNode node) => { return node.PartId == 0; });
        if (frameNode is null)
        {
            log.Error("Couldn't retrieve the target ImageNode of the gauge bar.");
            failed = true;
            return;
        }
        if (tickerNode.imageNode is null && !failed)
        {
            var hq = frameNode->PartsList->Parts[frameNode->PartId].UldAsset->AtkTexture.Resource->Version == 2;
            tickerNode.CreateCompleteImageNode(0, hq, (AtkResNode*)gaugeBase->AtkComponentBase.OwnerNode, &frameNode->AtkResNode);
            if (tickerNode.imageNode is null)
            {
                log.Error("ImageNode {id} could not be created.", tickerNode.NodeId);
                failed = true;
                return;
            }
            tickerNode.imageNode->AtkResNode.SetWidth(160);
            tickerNode.imageNode->AtkResNode.SetHeight(20);
        }
    }
    private unsafe void NativeUiDisposeListener(AddonEvent type, AddonArgs args)
    {
        primaryTickerNode.DestroyNode();
        secondaryTickerNode.DestroyNode();
    }

    public void Dispose()
    {
#if DEBUG
        DevWindow.Dispose();
#endif
        cts.Cancel();
        cts.Dispose();
        penumbraApi?.Dispose();
        receiveActorUpdateHook?.Disable();
        receiveActorUpdateHook?.Dispose();
        commandManager.RemoveHandler(CommandName);
        DebugWindow.Dispose();
        WindowSystem.RemoveAllWindows();

        framework.Update -= OnFrameworkUpdate;
        addonLifecycle.UnregisterListener(AddonEvent.PreFinalize, "_ParameterWidget", NativeUiDisposeListener);
        addonLifecycle.UnregisterListener(AddonEvent.PostUpdate, addonsLookup, CheckBarCollision);
        primaryTickerNode.Dispose();
        secondaryTickerNode.Dispose();
        clientState.TerritoryChanged -= TerritoryChanged;
        gameConfig.SystemChanged -= CheckResolutionChange;
        pluginInterface.UiBuilder.Draw -= WindowSystem.Draw;
        pluginInterface.UiBuilder.OpenConfigUi -= ConfigWindow.Toggle;
    }

    private void OnCommand(string command, string args)
    {
        ConfigWindow.Toggle();
    }
}

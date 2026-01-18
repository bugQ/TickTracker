using System;
using System.Numerics;

using Dalamud.Plugin;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;
using TickTracker.IPC;

namespace TickTracker.Windows;

public class ConfigWindow : Window
{
    private readonly Vector4 defaultDarkGrey = new(0.246f, 0.262f, 0.270f, 1f); // #3F4345
    private readonly Vector4 defaultBlue = new(0.169f, 0.747f, 0.892f, 1f); // #2BBEE3
    private readonly Vector4 defaultNativeBlue = new(0.256f, 0.708f, 0.934f, 1f);
    private readonly Vector4 defaultPink = new(0.753f, 0.271f, 0.482f, 1f); // #C0457B
    private readonly Vector4 defaultGreen = new(0.276f, 0.8f, 0.24f, 1f); // #46CC3D
    private readonly Vector4 defaultBlack = new(0f, 0f, 0f, 1f); // #000000
    private readonly Vector4 defaultWhite = new(1f, 1f, 1f, 1f); // #FFFFFF

    private readonly IDalamudPluginInterface pluginInterface;
    private readonly DebugWindow debugWindow;
    private readonly Configuration config;
    private readonly PenumbraApi? penumbraApi;

    private bool nativeDisabled;

    private const ImGuiColorEditFlags ColorEditFlags = ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.AlphaPreviewHalf | ImGuiColorEditFlags.AlphaBar;

    public ConfigWindow(IDalamudPluginInterface _pluginInterface, Configuration _config, DebugWindow _debugWindow, PenumbraApi? _penumbraApi) : base("TickTracker Settings")
    {
        Size = new Vector2(320, 390);
        Flags = ImGuiWindowFlags.NoResize;
        pluginInterface = _pluginInterface;
        config = _config;
        debugWindow = _debugWindow;
        penumbraApi = _penumbraApi;
    }

    public override void OnClose()
    {
        config.Save(pluginInterface);
    }

    public override void Draw()
    {
        nativeDisabled = penumbraApi is not null && penumbraApi.NativeUiBanned;
        EditConfigProperty("Lock bar size and position", config, c => c.LockBar, (c, value) => c.LockBar = value, checkbox: true);
        using (var child = ImRaii.Child("TabBarChild", new Vector2(ImGui.GetContentRegionAvail().X, ImGui.GetContentRegionAvail().Y - (36f * ImGuiHelpers.GlobalScale)), border: false))
        {
            using var tabBar = ImRaii.TabBar("ConfigTabBar");
            HealthTab();
            ManaTab();
            GatheringTab();
            SettingsTab();
        }
        DrawCloseButton();
    }

    private void HealthTab()
    {
        using var healthTab = ImRaii.TabItem("Health");
        if (!healthTab)
        {
            return;
        }
        ImGui.Spacing();
        var disabled = !config.HPVisible;

        EditConfigProperty("Show HP Bar", config, c => c.HPVisible, (c, value) => c.HPVisible = value, checkbox: true);
        ImGui.BeginDisabled(nativeDisabled);
        EditConfigProperty("Use NativeUi HP Bar", config, c => c.HPNativeUiVisible, (c, value) => c.HPNativeUiVisible = value, checkbox: true);
        ImGui.SameLine();
        ImGuiComponents.HelpMarker("This will make the frame around the game's native HP Bar to fill up to represent the tick progress.");
        ImGui.EndDisabled();
        ImGui.Spacing();
        DrawOptionsHP(ColorEditFlags, disabled);
        ImGui.Spacing();

        var HPBarPosition = config.HPBarPosition;
        var HPBarSize = config.HPBarSize;
        if (config.LockBar)
        {
            disabled = true;
        }
        if (DragInput2(ref HPBarPosition, "HPPositionX", "HPPositionY", "HP Bar Position", disabled))
        {
            config.HPBarPosition = HPBarPosition;
            config.Save(pluginInterface);
        }
        if (DragInput2(ref HPBarSize, "HPSizeX", "HPSizeY", "HP Bar Size", disabled, size: true))
        {
            config.HPBarSize = HPBarSize;
            config.Save(pluginInterface);
        }
    }

    private void ManaTab()
    {
        using var manaTab = ImRaii.TabItem("Mana");
        if (!manaTab)
        {
            return;
        }
        ImGui.Spacing();
        var disabled = !config.MPVisible;
        var fullyDisabled = disabled & !config.MPNativeUiVisible;

        EditConfigProperty("Show MP Bar", config, c => c.MPVisible, (c, value) => c.MPVisible = value, checkbox: true);
        ImGui.BeginDisabled(nativeDisabled);
        EditConfigProperty("Use NativeUi MP Bar", config, c => c.MPNativeUiVisible, (c, value) => c.MPNativeUiVisible = value, checkbox: true);
        ImGui.SameLine();
        ImGuiComponents.HelpMarker("This will make the frame around the game's native MP Bar to fill up to represent the tick progress.");
        ImGui.EndDisabled();
        ImGui.BeginDisabled(fullyDisabled);
        EditConfigProperty("Hide MP bar on Physical DPS", config, c => c.HideMpBarOnPhysicalDPS, (c, value) => c.HideMpBarOnPhysicalDPS = value, checkbox: true);
        ImGui.EndDisabled();
        ImGui.Spacing();

        DrawOptionsMP(ColorEditFlags, disabled);
        ImGui.Spacing();

        var MPBarPosition = config.MPBarPosition;
        var MPBarSize = config.MPBarSize;
        if (config.LockBar)
        {
            disabled = true;
        }
        if (DragInput2(ref MPBarPosition, "MPPositionX", "MPPositionY", "MP Bar Position", disabled))
        {
            config.MPBarPosition = MPBarPosition;
            config.Save(pluginInterface);
        }
        if (DragInput2(ref MPBarSize, "MPSizeX", "MPSizeY", "MP Bar Size", disabled, size: true))
        {
            config.MPBarSize = MPBarSize;
            config.Save(pluginInterface);
        }
    }

    private void GatheringTab()
    {
        using var gatheringTab = ImRaii.TabItem("Gathering");
        if (!gatheringTab)
        {
            return;
        }
        ImGui.Spacing();
        var disabled = !config.GPVisible;

        EditConfigProperty("Show GP Bar", config, c => c.GPVisible, (c, value) => c.GPVisible = value, checkbox: true);
        ImGui.SameLine();
        ImGuiComponents.HelpMarker("Only shown while a Disciple of Land job is active or bars are unlocked.");
        ImGui.BeginDisabled(nativeDisabled);
        EditConfigProperty("Use NativeUi GP Bar", config, c => c.GPNativeUiVisible, (c, value) => c.GPNativeUiVisible = value, checkbox: true);
        ImGui.SameLine();
        ImGuiComponents.HelpMarker("This will make the frame around the game's native GP Bar to fill up to represent the tick progress.");
        ImGui.EndDisabled();
        ImGui.Spacing();

        DrawOptionsGP(ColorEditFlags, disabled);
        ImGui.Spacing();

        var GPBarPosition = config.GPBarPosition;
        var GPBarSize = config.GPBarSize;
        if (config.LockBar)
        {
            disabled = true;
        }
        if (DragInput2(ref GPBarPosition, "GPPositionX", "GPPositionY", "GP Bar Position", disabled))
        {
            config.GPBarPosition = GPBarPosition;
            config.Save(pluginInterface);
        }

        if (DragInput2(ref GPBarSize, "GPSizeX", "GPSizeY", "GP Bar Size", disabled, size: true))
        {
            config.GPBarSize = GPBarSize;
            config.Save(pluginInterface);
        }
    }

    private void SettingsTab()
    {
        using var settingsTab = ImRaii.TabItem("Settings##Settings2");
        if (!settingsTab)
        {
            return;
        }
        ImGui.Spacing();

        EditConfigProperty("Hide bar on collision with native ui", config, c => c.CollisionDetection, (c, value) => c.CollisionDetection = value, checkbox: true);
        ImGui.SameLine();
        ImGuiComponents.HelpMarker("This only affects certain windows.");
        EditConfigProperty("Hide bar on full resource", config, c => c.HideOnFullResource, (c, value) => c.HideOnFullResource = value, checkbox: true);

        ImGui.Indent();
        EditConfigProperty("Always show in combat", config, c => c.AlwaysShowInCombat, (c, value) => c.AlwaysShowInCombat = value, checkbox: true);
        ImGui.Unindent();

        EditConfigProperty("Show only in combat", config, c => c.ShowOnlyInCombat, (c, value) => c.ShowOnlyInCombat = value, checkbox: true);

        ImGui.Indent();
        EditConfigProperty("Always show while in duties", config, c => c.AlwaysShowInDuties, (c, value) => c.AlwaysShowInDuties = value, checkbox: true);
        EditConfigProperty("Always show with enemy target", config, c => c.AlwaysShowWithHostileTarget, (c, value) => c.AlwaysShowWithHostileTarget = value, checkbox: true);
        EditConfigProperty("Disable collision detection while in combat", config, c => c.DisableCollisionInCombat, (c, value) => c.DisableCollisionInCombat = value, checkbox: true);
        ImGui.Unindent();
    }

    private void DrawCloseButton()
    {
        var originPos = ImGui.GetCursorPos();
        // Place a button in the bottom left
        ImGui.SetCursorPosX(10f);
        ImGui.SetCursorPosY(ImGui.GetWindowContentRegionMax().Y - ImGui.GetFrameHeight() - 5f + (ImGui.GetScrollY() * 2));
        if (ImGui.Button("Debug"))
        {
            debugWindow.Toggle();
        }
        ImGui.SetCursorPos(originPos);
        // Place a button in the bottom right + some padding / extra space
        ImGui.SetCursorPosX(ImGui.GetWindowContentRegionMax().X - ImGui.CalcTextSize("Close").X - 10f);
        ImGui.SetCursorPosY(ImGui.GetWindowContentRegionMax().Y - ImGui.GetFrameHeight() - 5f + (ImGui.GetScrollY() * 2));
        if (ImGui.Button("Close"))
        {
            config.Save(pluginInterface);
            IsOpen = false;
        }
        ImGui.SetCursorPos(originPos);
    }

    private void DrawOptionsHP(ImGuiColorEditFlags flags, bool disabled)
    {
        if (disabled)
        {
            ImGui.BeginDisabled(disabled);
        }

        EditConfigProperty("HP Bar Background Color", config, c => c.HPBarBackgroundColor, (c, value) => c.HPBarBackgroundColor = value, checkbox: false, button: false, colorEdit: true, flags);
        ImGui.SameLine();
        var resetButtonX = ImGui.GetCursorPosX();
        EditConfigProperty("Reset##ResetHPBarBackgroundColor", config, c => c.HPBarBackgroundColor, (c, value) => c.HPBarBackgroundColor = value, checkbox: false, button: true, colorEdit: false, flags, defaultBlack);

        EditConfigProperty("HP Bar Fill Color", config, c => c.HPBarFillColor, (c, value) => c.HPBarFillColor = value, checkbox: false, button: false, colorEdit: true, flags);
        ImGui.SameLine();
        ImGui.SetCursorPosX(resetButtonX);
        EditConfigProperty("Reset##ResetHPBarFillColor", config, c => c.HPBarFillColor, (c, value) => c.HPBarFillColor = value, checkbox: false, button: true, colorEdit: false, flags, defaultGreen);

        EditConfigProperty("HP Bar Border Color", config, c => c.HPBarBorderColor, (c, value) => c.HPBarBorderColor = value, checkbox: false, button: false, colorEdit: true, flags);
        ImGui.SameLine();
        ImGui.SetCursorPosX(resetButtonX);
        EditConfigProperty("Reset##ResetHPBarBorderColor", config, c => c.HPBarBorderColor, (c, value) => c.HPBarBorderColor = value, checkbox: false, button: true, colorEdit: false, flags, defaultDarkGrey);

        EditConfigProperty("HP Icon Color", config, c => c.HPIconColor, (c, value) => c.HPIconColor = value, checkbox: false, button: false, colorEdit: true, flags);
        ImGui.SameLine();
        ImGui.SetCursorPosX(resetButtonX);
        EditConfigProperty("Reset##ResetHPIconColor", config, c => c.HPIconColor, (c, value) => c.HPIconColor = value, checkbox: false, button: true, colorEdit: false, flags, defaultWhite);

        if (disabled)
        {
            ImGui.EndDisabled();
        }

        if (!config.HPNativeUiVisible)
        {
            ImGui.BeginDisabled();
        }

        EditConfigProperty("HP Native UI Color", config, c => c.HPNativeUiColor, (c, value) => c.HPNativeUiColor = value, checkbox: false, button: false, colorEdit: true, flags);
        ImGui.SameLine();
        ImGui.SetCursorPosX(resetButtonX);
        EditConfigProperty("Reset##ResetHPNativeUIColor", config, c => c.HPNativeUiColor, (c, value) => c.HPNativeUiColor = value, checkbox: false, button: true, colorEdit: false, flags, defaultNativeBlue);

        if (!config.HPNativeUiVisible)
        {
            ImGui.EndDisabled();
        }
    }

    private void DrawOptionsMP(ImGuiColorEditFlags flags, bool disabled)
    {
        if (disabled)
        {
            ImGui.BeginDisabled();
        }

        EditConfigProperty("MP Bar Background Color", config, c => c.MPBarBackgroundColor, (c, value) => c.MPBarBackgroundColor = value, checkbox: false, button: false, colorEdit: true, flags);
        ImGui.SameLine();
        var resetButtonX = ImGui.GetCursorPosX();
        EditConfigProperty("Reset##ResetMPBarBackgroundColor", config, c => c.MPBarBackgroundColor, (c, value) => c.MPBarBackgroundColor = value, checkbox: false, button: true, colorEdit: false, flags, defaultBlack);

        EditConfigProperty("MP Bar Fill Color", config, c => c.MPBarFillColor, (c, value) => c.MPBarFillColor = value, checkbox: false, button: false, colorEdit: true, flags);
        ImGui.SameLine();
        ImGui.SetCursorPosX(resetButtonX);
        EditConfigProperty("Reset##ResetMPBarFillColor", config, c => c.MPBarFillColor, (c, value) => c.MPBarFillColor = value, checkbox: false, button: true, colorEdit: false, flags, defaultPink);

        EditConfigProperty("MP Bar Border Color", config, c => c.MPBarBorderColor, (c, value) => c.MPBarBorderColor = value, checkbox: false, button: false, colorEdit: true, flags);
        ImGui.SameLine();
        ImGui.SetCursorPosX(resetButtonX);
        EditConfigProperty("Reset##ResetMPBarBorderColor", config, c => c.MPBarBorderColor, (c, value) => c.MPBarBorderColor = value, checkbox: false, button: true, colorEdit: false, flags, defaultDarkGrey);

        EditConfigProperty("MP Icon Color", config, c => c.MPIconColor, (c, value) => c.MPIconColor = value, checkbox: false, button: false, colorEdit: true, flags);
        ImGui.SameLine();
        ImGui.SetCursorPosX(resetButtonX);
        EditConfigProperty("Reset##ResetMPIconColor", config, c => c.MPIconColor, (c, value) => c.MPIconColor = value, checkbox: false, button: true, colorEdit: false, flags, defaultWhite);

        if (disabled)
        {
            ImGui.EndDisabled();
        }

        if (!config.MPNativeUiVisible)
        {
            ImGui.BeginDisabled();
        }

        EditConfigProperty("MP Native UI Color", config, c => c.MPNativeUiColor, (c, value) => c.MPNativeUiColor = value, checkbox: false, button: false, colorEdit: true, flags);
        ImGui.SameLine();
        ImGui.SetCursorPosX(resetButtonX);
        EditConfigProperty("Reset##ResetMPNativeUIColor", config, c => c.MPNativeUiColor, (c, value) => c.MPNativeUiColor = value, checkbox: false, button: true, colorEdit: false, flags, defaultNativeBlue);

        if (!config.MPNativeUiVisible)
        {
            ImGui.EndDisabled();
        }
    }

    private void DrawOptionsGP(ImGuiColorEditFlags flags, bool disabled)
    {
        if (disabled)
        {
            ImGui.BeginDisabled();
        }

        EditConfigProperty("GP Bar Background Color", config, c => c.GPBarBackgroundColor, (c, value) => c.GPBarBackgroundColor = value, checkbox: false, button: false, colorEdit: true, flags);
        ImGui.SameLine();
        var resetButtonX = ImGui.GetCursorPosX();
        EditConfigProperty("Reset##ResetGPBarBackgroundColor", config, c => c.GPBarBackgroundColor, (c, value) => c.GPBarBackgroundColor = value, checkbox: false, button: true, colorEdit: false, flags, defaultBlack);

        EditConfigProperty("GP Bar Fill Color", config, c => c.GPBarFillColor, (c, value) => c.GPBarFillColor = value, checkbox: false, button: false, colorEdit: true, flags);
        ImGui.SameLine();
        ImGui.SetCursorPosX(resetButtonX);
        EditConfigProperty("Reset##ResetGPBarFillColor", config, c => c.GPBarFillColor, (c, value) => c.GPBarFillColor = value, checkbox: false, button: true, colorEdit: false, flags, defaultBlue);

        EditConfigProperty("GP Bar Border Color", config, c => c.GPBarBorderColor, (c, value) => c.GPBarBorderColor = value, checkbox: false, button: false, colorEdit: true, flags);
        ImGui.SameLine();
        ImGui.SetCursorPosX(resetButtonX);
        EditConfigProperty("Reset##ResetGPBarBorderColor", config, c => c.GPBarBorderColor, (c, value) => c.GPBarBorderColor = value, checkbox: false, button: true, colorEdit: false, flags, defaultDarkGrey);

        EditConfigProperty("GP Icon Color", config, c => c.GPIconColor, (c, value) => c.GPIconColor = value, checkbox: false, button: false, colorEdit: true, flags);
        ImGui.SameLine();
        ImGui.SetCursorPosX(resetButtonX);
        EditConfigProperty("Reset##ResetGPIconColor", config, c => c.GPIconColor, (c, value) => c.GPIconColor = value, checkbox: false, button: true, colorEdit: false, flags, defaultWhite);

        if (disabled)
        {
            ImGui.EndDisabled();
        }

        if (!config.GPNativeUiVisible)
        {
            ImGui.BeginDisabled();
        }

        EditConfigProperty("GP Native UI Color", config, c => c.GPNativeUiColor, (c, value) => c.GPNativeUiColor = value, checkbox: false, button: false, colorEdit: true, flags);
        ImGui.SameLine();
        ImGui.SetCursorPosX(resetButtonX);
        EditConfigProperty("Reset##ResetGPNativeUIColor", config, c => c.GPNativeUiColor, (c, value) => c.GPNativeUiColor = value, checkbox: false, button: true, colorEdit: false, flags, defaultNativeBlue);

        if (!config.GPNativeUiVisible)
        {
            ImGui.EndDisabled();
        }
    }

    /// <summary>
    ///     The <see cref="ImGui.DragInt2" /> we have at home.
    /// </summary>
    private static bool DragInput2(ref Vector2 vector, string label1, string label2, string description, bool disabled = false, bool size = false)
    {
        var change = false;
        var x = (int)vector.X;
        var y = (int)vector.Y;
        var minValue = size switch
        {
            true => 32,
            _ => 0,
        };
        if (disabled)
        {
            ImGui.BeginDisabled();
        }

        ImGui.PushItemWidth(ImGui.GetContentRegionMax().X / 3.5f);
        if (ImGui.DragInt($"##{label1}", ref x, 1, minValue, (int)TickTracker.Resolution.X, "%d", ImGuiSliderFlags.AlwaysClamp))
        {
            vector.X = x;
            change = true;
        }
        ImGui.PopItemWidth();
        ImGui.SameLine();

        ImGui.PushItemWidth(ImGui.GetContentRegionMax().X / 3.5f);
        if (ImGui.DragInt($"##{label2}", ref y, 1, minValue, (int)TickTracker.Resolution.Y, "%d", ImGuiSliderFlags.AlwaysClamp))
        {
            vector.Y = y;
            change = true;
        }
        ImGui.PopItemWidth();
        ImGui.SameLine();
        ImGui.TextUnformatted(description);

        if (disabled)
        {
            ImGui.EndDisabled();
        }
        return change;
    }

    // A truly horrific piece of code
    private void EditConfigProperty<T>(string label, Configuration _config, Func<Configuration, T> getProperty, Action<Configuration, T> setProperty, bool checkbox = false, bool button = false, bool colorEdit = false, ImGuiColorEditFlags flags = ImGuiColorEditFlags.None, Vector4 defaultColor = default)
    {
        // a T that's a Vector4 and has both button and colorEdit set as true would erroneously be forced to a button,
        // alternatively would have both of them present, which is an invalid use of the function

        var onlyCheckbox = checkbox && !button && !colorEdit;
        var onlyButton = button && !checkbox && !colorEdit;
        var onlyColorEdit = colorEdit && !button && !checkbox;

        if (typeof(T) == typeof(bool) && onlyCheckbox)
        {
            var option = (bool)(object)getProperty(_config)!;
            if (ImGui.Checkbox(label, ref option))
            {
                setProperty(_config, (T)(object)option);
                _config.Save(pluginInterface);
            }
        }
        else if (typeof(T) == typeof(Vector4) && onlyButton)
        {
            if (ImGui.Button(label))
            {
                setProperty(_config, (T)(object)defaultColor);
                _config.Save(pluginInterface);
            }
        }
        else if (typeof(T) == typeof(Vector4) && onlyColorEdit)
        {
            var vectorValue = (Vector4)(object)getProperty(_config)!;
            if (ImGui.ColorEdit4(label, ref vectorValue, flags))
            {
                setProperty(_config, (T)(object)vectorValue);
                _config.Save(pluginInterface);
            }
        }
        else
        {
            ImGui.TextUnformatted("Invalid EditConfigProperty invocation.");
        }
    }
}

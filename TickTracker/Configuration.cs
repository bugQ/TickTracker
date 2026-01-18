using System.Numerics;

using Dalamud.Configuration;
using Dalamud.Plugin;

namespace TickTracker;

public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 0;
    public bool LockBar { get; set; } = false;
    public bool ShowOnlyInCombat { get; set; } = false;
    public bool AlwaysShowInDuties { get; set; } = false;
    public bool AlwaysShowWithHostileTarget { get; set; } = false;
    public bool AlwaysShowInCombat { get; set; } = false;
    public bool HPVisible { get; set; } = true;
    public bool HPNativeUiVisible { get; set; } = false;
    public bool MPVisible { get; set; } = true;
    public bool MPNativeUiVisible { get; set; } = false;
    public bool GPVisible { get; set; } = true;
    public bool GPNativeUiVisible { get; set; } = false;
    public bool HideOnFullResource { get; set; } = false;
    public bool DisableCollisionInCombat { get; set; } = false;
    public bool CollisionDetection { get; set; } = false;
    public bool HideMpBarOnPhysicalDPS { get; set; } = false;

    public Vector2 HPBarPosition { get; set; } = new(600, 500);
    public Vector2 HPBarSize { get; set; } = new(180, 50);
    public Vector4 HPBarBackgroundColor { get; set; } = new(0f, 0f, 0f, 1f);
    public Vector4 HPBarFillColor { get; set; } = new(0.276f, 0.8f, 0.24f, 1f);
    public Vector4 HPBarBorderColor { get; set; } = new(0.246f, 0.262f, 0.270f, 1f);
    public Vector4 HPNativeUiColor { get; set; } = new(0f, 0.570f, 0.855f, 1f);
    public Vector4 HPIconColor { get; set; } = new(1f, 1f, 1f, 1f);

    public Vector2 MPBarPosition { get; set; } = new(900, 500);
    public Vector2 MPBarSize { get; set; } = new(180, 50);
    public Vector4 MPBarBackgroundColor { get; set; } = new(0f, 0f, 0f, 1f);
    public Vector4 MPBarFillColor { get; set; } = new(0.753f, 0.271f, 0.482f, 1f);
    public Vector4 MPBarBorderColor { get; set; } = new(0.246f, 0.262f, 0.270f, 1f);
    public Vector4 MPNativeUiColor { get; set; } = new(0f, 0.570f, 0.855f, 1f);
    public Vector4 MPIconColor { get; set; } = new(1f, 1f, 1f, 1f);

    public Vector2 GPBarPosition { get; set; } = new(750, 400);
    public Vector2 GPBarSize { get; set; } = new(180, 50);
    public Vector4 GPBarBackgroundColor { get; set; } = new(0f, 0f, 0f, 1f);
    public Vector4 GPBarFillColor { get; set; } = new(0.169f, 0.747f, 0.892f, 1f);
    public Vector4 GPBarBorderColor { get; set; } = new(0.246f, 0.262f, 0.270f, 1f);
    public Vector4 GPNativeUiColor { get; set; } = new(0f, 0.570f, 0.855f, 1f);
    public Vector4 GPIconColor { get; set; } = new(1f, 1f, 1f, 1f);

    public void Save(IDalamudPluginInterface pi) => pi.SavePluginConfig(this);

}

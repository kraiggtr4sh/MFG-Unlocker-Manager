namespace MFGUnlockerManager.Models;

public sealed class MfgGameConfig
{
    /// <summary>
    /// From RTXMFG-Universal.json: "" (default), "shared", or "optiscaler"
    /// </summary>
    public string OptiScalerUi { get; set; } = string.Empty;

    /// <summary>
    /// From RTX40MFG-UI.ini [Overlay] ToggleKey (decimal VK code, default 8 = Backspace)
    /// </summary>
    public int ToggleKey { get; set; } = 8;

    /// <summary>
    /// From RTX40MFG-UI.ini [Compatibility] GpuNameOverride (1/0) - unlocks FG option in game menus on RTX 20/30
    /// </summary>
    public bool GpuNameOverride { get; set; }

    /// <summary>
    /// From RTX40MFG-UI.ini [Compatibility] Rtx30FlipMetering (1/0)
    /// </summary>
    public bool Rtx30FlipMetering { get; set; }

    /// <summary>
    /// From RTXMFG.RtxHair.ini [RtxHair] Universal (default true = 1, false = 0)
    /// </summary>
    public bool EnableRtxHair { get; set; } = true;

    /// <summary>
    /// Raw JSON content of RTXMFG-Universal.json
    /// </summary>
    public string RawUniversalJson { get; set; } = "{\n}";
}

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using MFGUnlockerManager.Models;

namespace MFGUnlockerManager.Services;

public sealed class MfgConfigService
{
    public static MfgGameConfig LoadConfig(string gameDirectory)
    {
        var config = new MfgGameConfig();
        if (string.IsNullOrWhiteSpace(gameDirectory) || !Directory.Exists(gameDirectory))
            return config;

        // 1. RTXMFG-Universal.json
        var jsonPath = Path.Combine(gameDirectory, "RTXMFG-Universal.json");
        if (File.Exists(jsonPath))
        {
            try
            {
                var raw = File.ReadAllText(jsonPath);
                config.RawUniversalJson = raw;
                var node = JsonNode.Parse(raw) as JsonObject;
                if (node != null && node.TryGetPropertyValue("optiScalerUi", out var uiVal) && uiVal != null)
                {
                    config.OptiScalerUi = uiVal.GetValue<string>();
                }
            }
            catch
            {
            }
        }

        // 2. RTX40MFG-UI.ini
        var iniPath = Path.Combine(gameDirectory, "RTX40MFG-UI.ini");
        if (File.Exists(iniPath))
        {
            try
            {
                var iniText = File.ReadAllText(iniPath);
                var toggleMatch = Regex.Match(iniText, @"^\s*ToggleKey\s*=\s*(\d+)", RegexOptions.Multiline | RegexOptions.IgnoreCase);
                if (toggleMatch.Success && int.TryParse(toggleMatch.Groups[1].Value, out int vk))
                {
                    config.ToggleKey = vk;
                }

                var gpuMatch = Regex.Match(iniText, @"^\s*GpuNameOverride\s*=\s*(1|true)", RegexOptions.Multiline | RegexOptions.IgnoreCase);
                config.GpuNameOverride = gpuMatch.Success;

                var flipMatch = Regex.Match(iniText, @"^\s*Rtx30FlipMetering\s*=\s*(1|true)", RegexOptions.Multiline | RegexOptions.IgnoreCase);
                config.Rtx30FlipMetering = flipMatch.Success;
            }
            catch
            {
            }
        }

        // 3. RTXMFG.RtxHair.ini
        var hairPath = Path.Combine(gameDirectory, "RTXMFG.RtxHair.ini");
        if (File.Exists(hairPath))
        {
            try
            {
                var hairText = File.ReadAllText(hairPath);
                if (Regex.IsMatch(hairText, @"^\s*Universal\s*=\s*0", RegexOptions.Multiline | RegexOptions.IgnoreCase))
                {
                    config.EnableRtxHair = false;
                }
            }
            catch
            {
            }
        }

        return config;
    }

    public static void SaveConfig(string gameDirectory, MfgGameConfig config)
    {
        if (string.IsNullOrWhiteSpace(gameDirectory) || !Directory.Exists(gameDirectory))
            throw new DirectoryNotFoundException($"Game directory does not exist: {gameDirectory}");

        // 1. Update RTXMFG-Universal.json
        var jsonPath = Path.Combine(gameDirectory, "RTXMFG-Universal.json");
        JsonObject rootObj;
        try
        {
            rootObj = (JsonNode.Parse(config.RawUniversalJson) as JsonObject) ?? new JsonObject();
        }
        catch
        {
            rootObj = new JsonObject();
        }

        if (string.IsNullOrWhiteSpace(config.OptiScalerUi))
        {
            rootObj.Remove("optiScalerUi");
        }
        else
        {
            rootObj["optiScalerUi"] = config.OptiScalerUi;
        }

        var formattedJson = rootObj.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        config.RawUniversalJson = formattedJson;
        File.WriteAllText(jsonPath, formattedJson);

        // 2. Update RTX40MFG-UI.ini
        var iniPath = Path.Combine(gameDirectory, "RTX40MFG-UI.ini");
        var lines = new List<string>
        {
            "[Overlay]",
            $"ToggleKey={config.ToggleKey}",
            "",
            "[Compatibility]",
            $"GpuNameOverride={(config.GpuNameOverride ? 1 : 0)}",
            $"Rtx30FlipMetering={(config.Rtx30FlipMetering ? 1 : 0)}"
        };

        // Preserve any existing [Keybinds] section if file exists
        if (File.Exists(iniPath))
        {
            var oldText = File.ReadAllText(iniPath);
            int keybindsIdx = oldText.IndexOf("[Keybinds]", StringComparison.OrdinalIgnoreCase);
            if (keybindsIdx >= 0)
            {
                lines.Add("");
                lines.Add(oldText[keybindsIdx..].TrimEnd());
            }
        }

        File.WriteAllLines(iniPath, lines);

        // 3. Update RTXMFG.RtxHair.ini
        var hairPath = Path.Combine(gameDirectory, "RTXMFG.RtxHair.ini");
        if (!config.EnableRtxHair)
        {
            File.WriteAllText(hairPath, "[RtxHair]\r\nUniversal=0\r\n");
        }
        else if (File.Exists(hairPath))
        {
            File.Delete(hairPath);
        }
    }
}

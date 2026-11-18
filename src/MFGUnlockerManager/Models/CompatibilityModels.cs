namespace MFGUnlockerManager.Models;

public enum ProxyCandidateState
{
    Recommended,
    AlsoLoaded,
    Available,
    BinkHookRoute,
    OccupiedByOtherFile,
    DoNotUse
}

public sealed class GameCompatibilityEntry
{
    public string GameName { get; set; } = string.Empty;
    public List<string> RecommendedUse { get; set; } = [];
    public List<string> AlsoLoaded { get; set; } = [];
    public List<string> DoNotUse { get; set; } = [];
    public string RawUseColumn { get; set; } = string.Empty;
    public string RawAlsoColumn { get; set; } = string.Empty;
    public string RawDontUseColumn { get; set; } = string.Empty;
    public bool SupportsOptiScaler { get; set; }
    public bool NeedsCustomOptiScalerBuild { get; set; }
}

public sealed class ProxyCandidate
{
    public string FileName { get; set; } = string.Empty;
    public ProxyCandidateState State { get; set; }
    public string BadgeText { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsSelectable { get; set; } = true;
    public bool IsBink { get; set; }
}

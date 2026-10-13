using System.Text.Json;
using System.IO;

namespace XmlCompare.Models;

public sealed class ComparisonProfile
{
    public string Name { get; set; } = "Generic XML";
    public double AbsoluteTolerance { get; set; }
    public double RelativeTolerance { get; set; }
    public bool TrimWhitespace { get; set; } = true;
    public bool IgnoreCase { get; set; }
    public bool EmptyEqualsMissing { get; set; }
    public List<string> IgnoredPaths { get; set; } = [];
    public List<MatchRule> MatchRules { get; set; } = [];
    public List<LabelRule> LabelRules { get; set; } = [];

    public override string ToString() => Name;

    public static IReadOnlyList<ComparisonProfile> LoadAll(string directory)
    {
        var result = new List<ComparisonProfile>();
        if (!Directory.Exists(directory)) return result;
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        foreach (var file in Directory.EnumerateFiles(directory, "*.json").OrderBy(x => x))
        {
            try
            {
                var profile = JsonSerializer.Deserialize<ComparisonProfile>(File.ReadAllText(file), options);
                if (profile is not null) result.Add(profile);
            }
            catch
            {
                // A malformed custom profile must not prevent the application starting.
            }
        }
        return result;
    }
}

public sealed class MatchRule
{
    public string Path { get; set; } = "";
    public List<List<string>> Keys { get; set; } = [];
}

public sealed class LabelRule
{
    public string Path { get; set; } = "";
    public List<string> Fields { get; set; } = [];
}

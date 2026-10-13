using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using XmlCompare.Models;

namespace XmlCompare.Services;

public sealed class ComparisonEngine
{
    private static readonly string[] GenericKeyCandidates =
    [
        "internalidentifier", "id", "identifier", "key", "code", "fundcode", "rateindex", "chargecategory",
        "category", "month", "planyear", "year", "age", "source", "type", "name", "description"
    ];

    private ComparisonProfile _profile = new();
    private int _fileCount;

    public CompareNode Compare(IReadOnlyList<string> filePaths, ComparisonProfile profile,
        double? absoluteTolerance = null, double? relativeTolerance = null)
    {
        if (filePaths.Count is < 2 or > 3) throw new ArgumentException("Select two or three XML files.");
        _profile = profile;
        if (absoluteTolerance.HasValue) _profile.AbsoluteTolerance = absoluteTolerance.Value;
        if (relativeTolerance.HasValue) _profile.RelativeTolerance = relativeTolerance.Value;
        _fileCount = filePaths.Count;

        var documents = filePaths.Select(LoadSafe).ToArray();
        var roots = new XElement?[3];
        for (var i = 0; i < documents.Length; i++) roots[i] = documents[i].Root;
        if (roots.Take(_fileCount).Any(x => x is null)) throw new XmlException("One of the documents has no root element.");
        return BuildNode(roots, "/" + roots.First(x => x is not null)!.Name.LocalName);
    }

    private static XDocument LoadSafe(string path)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true
        };
        using var reader = XmlReader.Create(path, settings);
        return XDocument.Load(reader, LoadOptions.None);
    }

    private CompareNode BuildNode(XElement?[] elements, string path)
    {
        var first = elements.First(x => x is not null)!;
        var node = new CompareNode { Name = first.Name.LocalName, Path = path };
        for (var i = 0; i < _fileCount; i++)
        {
            node.Present[i] = elements[i] is not null;
            if (elements[i] is not null && !elements[i]!.Elements().Any())
                node.Values[i] = Normalize(elements[i]!.Value);
        }
        node.Label = MakeLabel(first, path);

        var elementNames = elements.Take(_fileCount).Where(x => x is not null).Select(x => x!.Name.ToString()).Distinct().ToList();
        if (elementNames.Count > 1)
        {
            var nameNode = new CompareNode { Name = "#element", Label = "#element", Path = path + "/#element", Parent = node };
            for (var i = 0; i < _fileCount; i++)
            {
                nameNode.Present[i] = elements[i] is not null;
                nameNode.Values[i] = elements[i]?.Name.ToString();
            }
            FinalizeNode(nameNode);
            node.Children.Add(nameNode);
        }

        AddAttributeNodes(node, elements, path);
        AddElementNodes(node, elements, path);
        FinalizeNode(node);
        return node;
    }

    private void AddAttributeNodes(CompareNode parent, XElement?[] elements, string path)
    {
        var names = elements.Where(x => x is not null)
            .SelectMany(x => x!.Attributes())
            .Where(a => !a.IsNamespaceDeclaration)
            .Select(a => a.Name)
            .Distinct()
            .OrderBy(x => x.LocalName, StringComparer.OrdinalIgnoreCase);

        foreach (var name in names)
        {
            var childPath = $"{path}/@{name.LocalName}";
            if (IsIgnored(childPath)) continue;
            var child = new CompareNode { Name = "@" + name.LocalName, Label = "@" + name.LocalName, Path = childPath };
            for (var i = 0; i < _fileCount; i++)
            {
                var attribute = elements[i]?.Attribute(name);
                child.Present[i] = attribute is not null;
                child.Values[i] = attribute is null ? null : Normalize(attribute.Value);
            }
            FinalizeNode(child);
            child.Parent = parent;
            parent.Children.Add(child);
        }
    }

    private void AddElementNodes(CompareNode parent, XElement?[] elements, string path)
    {
        var names = elements.Where(x => x is not null)
            .SelectMany(x => x!.Elements())
            .Select(x => x.Name)
            .Distinct()
            .OrderBy(x => x.LocalName, StringComparer.OrdinalIgnoreCase);

        foreach (var name in names)
        {
            var childPath = $"{path}/{name.LocalName}";
            if (IsIgnored(childPath)) continue;
            var lists = new List<XElement>[3];
            for (var i = 0; i < 3; i++) lists[i] = elements[i]?.Elements(name).ToList() ?? [];
            foreach (var aligned in Align(lists, childPath))
            {
                var child = BuildNode(aligned, childPath);
                child.Parent = parent;
                parent.Children.Add(child);
            }
        }
    }

    private IEnumerable<XElement?[]> Align(List<XElement>[] lists, string path)
    {
        if (lists.Take(_fileCount).All(x => x.Count <= 1))
        {
            yield return [lists[0].FirstOrDefault(), lists[1].FirstOrDefault(), lists[2].FirstOrDefault()];
            yield break;
        }

        var keyFields = FindUsableKey(lists, path);
        if (keyFields.Count > 0)
        {
            var maps = lists.Select(list => list
                .GroupBy(x => KeyFor(x, keyFields), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => new Queue<XElement>(g), StringComparer.OrdinalIgnoreCase)).ToArray();
            var keys = maps.Take(_fileCount).SelectMany(x => x.Keys).Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase);
            foreach (var key in keys)
            {
                var max = maps.Take(_fileCount).Max(m => m.TryGetValue(key, out var q) ? q.Count : 0);
                for (var occurrence = 0; occurrence < max; occurrence++)
                {
                    var row = new XElement?[3];
                    for (var i = 0; i < _fileCount; i++)
                        if (maps[i].TryGetValue(key, out var queue) && queue.Count > 0) row[i] = queue.Dequeue();
                    yield return row;
                }
            }
            yield break;
        }

        // Exact structural fingerprints handle pure reordering when no stable business key exists.
        var fingerprintMaps = lists.Select(list => list.GroupBy(Fingerprint)
            .ToDictionary(g => g.Key, g => new Queue<XElement>(g))).ToArray();
        var fingerprints = fingerprintMaps.Take(_fileCount).SelectMany(x => x.Keys).Distinct().OrderBy(x => x);
        var leftovers = new List<XElement>[3] { [], [], [] };
        foreach (var fingerprint in fingerprints)
        {
            var max = fingerprintMaps.Take(_fileCount).Max(m => m.TryGetValue(fingerprint, out var q) ? q.Count : 0);
            if (fingerprintMaps.Take(_fileCount).Count(m => m.ContainsKey(fingerprint)) > 1)
            {
                for (var occurrence = 0; occurrence < max; occurrence++)
                {
                    var row = new XElement?[3];
                    for (var i = 0; i < _fileCount; i++)
                        if (fingerprintMaps[i].TryGetValue(fingerprint, out var queue) && queue.Count > 0) row[i] = queue.Dequeue();
                    yield return row;
                }
            }
            else
            {
                for (var i = 0; i < _fileCount; i++)
                    if (fingerprintMaps[i].TryGetValue(fingerprint, out var queue)) leftovers[i].AddRange(queue);
            }
        }

        var leftoverCount = leftovers.Take(_fileCount).Max(x => x.Count);
        for (var index = 0; index < leftoverCount; index++)
            yield return [ElementAtOrNull(leftovers[0], index), ElementAtOrNull(leftovers[1], index), ElementAtOrNull(leftovers[2], index)];
    }

    private IReadOnlyList<string> FindUsableKey(List<XElement>[] lists, string path)
    {
        var configured = _profile.MatchRules.FirstOrDefault(x => PathMatches(path, x.Path));
        var candidates = configured?.Keys ?? GenericKeyCandidates.Select(x => new List<string> { x }).ToList();
        IReadOnlyList<string>? best = null;
        var bestOverlap = -1;
        foreach (var fields in candidates)
        {
            var any = false;
            var usable = true;
            var keySets = new List<HashSet<string>>();
            foreach (var list in lists.Take(_fileCount).Where(x => x.Count > 0))
            {
                var keys = list.Select(x => KeyFor(x, fields)).ToList();
                if (keys.Any(string.IsNullOrEmpty) || keys.Distinct(StringComparer.OrdinalIgnoreCase).Count() != keys.Count)
                {
                    usable = false;
                    break;
                }
                any = true;
                keySets.Add(keys.ToHashSet(StringComparer.OrdinalIgnoreCase));
            }
            if (!usable || !any) continue;

            // Prefer identities that actually connect records across documents. A generated
            // GUID can be unique inside every file while being useless as a comparison key.
            var overlap = 0;
            for (var left = 0; left < keySets.Count; left++)
                for (var right = left + 1; right < keySets.Count; right++)
                    overlap += keySets[left].Intersect(keySets[right], StringComparer.OrdinalIgnoreCase).Count();
            if (overlap > bestOverlap)
            {
                best = fields;
                bestOverlap = overlap;
            }
        }
        return best ?? [];
    }

    private static string KeyFor(XElement element, IReadOnlyList<string> fields) => string.Join(" | ", fields.Select(field =>
    {
        if (field.StartsWith('@')) return element.Attributes().FirstOrDefault(x => x.Name.LocalName.Equals(field[1..], StringComparison.OrdinalIgnoreCase))?.Value ?? "";
        return element.Elements().FirstOrDefault(x => x.Name.LocalName.Equals(field, StringComparison.OrdinalIgnoreCase))?.Value ?? "";
    }));

    private string MakeLabel(XElement element, string path)
    {
        var rule = _profile.LabelRules.FirstOrDefault(x => PathMatches(path, x.Path));
        var fields = rule?.Fields ?? FindUsableKey([element.Parent?.Elements(element.Name).ToList() ?? [element], [], []], path);
        var key = fields.Count > 0 ? KeyFor(element, fields) : "";
        return string.IsNullOrWhiteSpace(key) ? element.Name.LocalName : $"{element.Name.LocalName}: {key}";
    }

    private void FinalizeNode(CompareNode node)
    {
        if (node.Children.Count > 0)
        {
            if (!node.Present.Take(_fileCount).All(x => x))
            {
                node.Kind = DifferenceKind.Missing;
                node.DifferenceCount = 1;
                return;
            }
            var ownValueDifference = node.Values.Take(_fileCount).Any(x => x is not null) &&
                                     !node.Values.Take(_fileCount).Skip(1).All(x => ValuesEqual(node.Values[0] ?? "", x ?? "")) ? 1 : 0;
            node.Kind = ownValueDifference > 0 ? DifferenceKind.Changed : DifferenceKind.Container;
            node.DifferenceCount = ownValueDifference + node.Children.Sum(x => x.DifferenceCount);
            return;
        }
        var present = node.Present.Take(_fileCount).ToArray();
        if (!present.All(x => x))
        {
            if (_profile.EmptyEqualsMissing)
            {
                var presentValues = node.Values.Take(_fileCount).Where((_, i) => present[i]).ToArray();
                if (presentValues.All(string.IsNullOrEmpty)) { node.Kind = DifferenceKind.Equal; return; }
            }
            node.Kind = DifferenceKind.Missing;
            node.DifferenceCount = 1;
            return;
        }
        var first = node.Values[0] ?? "";
        node.Kind = node.Values.Take(_fileCount).Skip(1).All(x => ValuesEqual(first, x ?? ""))
            ? DifferenceKind.Equal : DifferenceKind.Changed;
        node.DifferenceCount = node.Kind == DifferenceKind.Equal ? 0 : 1;
    }

    private bool ValuesEqual(string left, string right)
    {
        var comparison = _profile.IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (string.Equals(left, right, comparison)) return true;
        if (double.TryParse(left, NumberStyles.Float, CultureInfo.InvariantCulture, out var a) &&
            double.TryParse(right, NumberStyles.Float, CultureInfo.InvariantCulture, out var b))
        {
            var difference = Math.Abs(a - b);
            if (difference <= _profile.AbsoluteTolerance) return true;
            var scale = Math.Max(Math.Abs(a), Math.Abs(b));
            return scale > 0 && difference / scale <= _profile.RelativeTolerance;
        }
        return false;
    }

    private string Normalize(string value) => _profile.TrimWhitespace ? value.Trim() : value;
    private bool IsIgnored(string path) => _profile.IgnoredPaths.Any(x => PathMatches(path, x));
    private static bool PathMatches(string path, string pattern) => pattern.StartsWith('/')
        ? path.Equals(pattern, StringComparison.OrdinalIgnoreCase) || path.EndsWith(pattern, StringComparison.OrdinalIgnoreCase)
        : path.EndsWith('/' + pattern.TrimStart('/'), StringComparison.OrdinalIgnoreCase);
    private static XElement? ElementAtOrNull(List<XElement> list, int index) => index < list.Count ? list[index] : null;

    private static string Fingerprint(XElement element)
    {
        var canonical = Canonical(element);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static string Canonical(XElement element)
    {
        var attributes = element.Attributes().Where(x => !x.IsNamespaceDeclaration)
            .OrderBy(x => x.Name.ToString()).Select(x => $"@{x.Name}={x.Value}");
        var children = element.Elements().Select(Canonical).OrderBy(x => x, StringComparer.Ordinal);
        var value = element.HasElements ? "" : element.Value.Trim();
        return $"<{element.Name}|{string.Join(';', attributes)}|{value}|{string.Join(';', children)}>";
    }
}

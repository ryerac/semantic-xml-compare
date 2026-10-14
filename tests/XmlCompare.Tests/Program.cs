using System.Xml.Linq;
using System.Diagnostics;
using XmlCompare.Models;
using XmlCompare.Services;

if (args.Length == 2)
{
    var profiles = ComparisonProfile.LoadAll(Path.Combine(Directory.GetCurrentDirectory(), "profiles"));
    var imagoProfile = profiles.First(x => x.Name == "Imago");
    var result = new ComparisonEngine().Compare(args, imagoProfile);
    Console.WriteLine($"Differences: {result.DifferenceCount:N0}");
    foreach (var node in Descendants(result).Where(x => x.Kind is DifferenceKind.Changed or DifferenceKind.Missing).Take(30))
        Console.WriteLine($"{node.Kind}: {node.Path} | {node.Value1} | {node.Value2}");
    return;
}

var profile = new ComparisonProfile
{
    Name = "Test",
    AbsoluteTolerance = 0.01,
    MatchRules = [new MatchRule { Path = "item", Keys = [["id"]] }],
    LabelRules = [new LabelRule { Path = "item", Fields = ["id"] }]
};

var left = "<root><items><item><id>A</id><value>10</value></item><item><id>B</id><value>20</value></item></items></root>";
var reorderedWithinTolerance = "<root><items><item><id>B</id><value>20.005</value></item><item><id>A</id><value>10</value></item></items></root>";
var changedAndAdded = "<root><items><item><id>B</id><value>21</value></item><item><id>A</id><value>10</value></item><item><id>C</id><value>30</value></item></items></root>";

var temp = Path.Combine(Path.GetTempPath(), "xml-compare-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temp);
try
{
    var files = new[] { Path.Combine(temp, "a.xml"), Path.Combine(temp, "b.xml"), Path.Combine(temp, "c.xml") };
    File.WriteAllText(files[0], left);
    File.WriteAllText(files[1], reorderedWithinTolerance);
    File.WriteAllText(files[2], changedAndAdded);

    var engine = new ComparisonEngine();
    var equal = engine.Compare(files[..2], profile);
    Assert(equal.DifferenceCount == 0, "reordering and values within tolerance should compare equal");

    var different = engine.Compare(files, profile);
    Assert(different.DifferenceCount == 2, "one changed value and one added item should produce two differences");
    var items = Descendants(different).Where(x => x.Name == "item").ToList();
    Assert(items.Any(x => x.Label.Contains("C") && x.DifferenceCount == 1), "added item C should remain a logical block");

    var regeneratedIdsLeft = Path.Combine(temp, "ids-left.xml");
    var regeneratedIdsRight = Path.Combine(temp, "ids-right.xml");
    File.WriteAllText(regeneratedIdsLeft, "<root><fund><internalidentifier>old-a</internalidentifier><fundcode>A</fundcode><value>1</value></fund><fund><internalidentifier>old-b</internalidentifier><fundcode>B</fundcode><value>2</value></fund></root>");
    File.WriteAllText(regeneratedIdsRight, "<root><fund><internalidentifier>new-b</internalidentifier><fundcode>B</fundcode><value>2</value></fund><fund><internalidentifier>new-a</internalidentifier><fundcode>A</fundcode><value>1</value></fund></root>");
    var regeneratedProfile = new ComparisonProfile
    {
        MatchRules = [new MatchRule { Path = "fund", Keys = [["internalidentifier"], ["fundcode"]] }]
    };
    var regenerated = engine.Compare([regeneratedIdsLeft, regeneratedIdsRight], regeneratedProfile);
    Assert(regenerated.DifferenceCount == 2, "stable fund codes should match blocks even when generated IDs change");
    Assert(Descendants(regenerated).Count(x => x.Name == "fund") == 2, "regenerated IDs must not split two funds into four blocks");

    var attributedLeft = Path.Combine(temp, "attributed-left.xml");
    var attributedRight = Path.Combine(temp, "attributed-right.xml");
    File.WriteAllText(attributedLeft, "<root><value unit='GBP'>10</value></root>");
    File.WriteAllText(attributedRight, "<root><value unit='USD'>11</value></root>");
    var attributed = engine.Compare([attributedLeft, attributedRight], new ComparisonProfile());
    Assert(attributed.DifferenceCount == 2, "attribute and text differences on the same leaf should both be counted");

    var otherRoot = Path.Combine(temp, "other-root.xml");
    File.WriteAllText(otherRoot, "<different><items /></different>");
    var rootMismatch = engine.Compare([files[0], otherRoot], new ComparisonProfile());
    Assert(rootMismatch.DifferenceCount > 0, "different root element names should be reported");

    var hostile = Path.Combine(temp, "hostile.xml");
    File.WriteAllText(hostile, "<!DOCTYPE root [<!ENTITY xxe SYSTEM 'file:///c:/windows/win.ini'>]><root>&xxe;</root>");
    var rejected = false;
    try { engine.Compare([files[0], hostile], profile); }
    catch (System.Xml.XmlException) { rejected = true; }
    Assert(rejected, "DTD input should be rejected");

    var sample = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "sample", "1.xml"));
    var profileDirectory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "profiles"));
    if (File.Exists(sample))
    {
        var imago = ComparisonProfile.LoadAll(profileDirectory).First(x => x.Name == "Imago");
        var stopwatch = Stopwatch.StartNew();
        var sampleResult = engine.Compare([sample, sample], imago);
        stopwatch.Stop();
        if (sampleResult.DifferenceCount != 0)
            foreach (var node in Descendants(sampleResult).Where(x => x.DifferenceCount > 0 && x.Children.Count == 0).Take(20))
                Console.WriteLine($"Unexpected sample diff: {node.Path} | '{node.Value1}' | '{node.Value2}' | {node.Kind}");
        Assert(sampleResult.DifferenceCount == 0, "the supplied sample should equal itself");
        Console.WriteLine($"Sample comparison passed in {stopwatch.ElapsedMilliseconds:N0} ms.");
    }

    var demoBefore = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples", "catalogue-before.xml"));
    var demoAfter = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples", "catalogue-after.xml"));
    if (File.Exists(demoBefore) && File.Exists(demoAfter))
    {
        var exactDemo = engine.Compare([demoBefore, demoAfter], new ComparisonProfile());
        Assert(exactDemo.DifferenceCount == 5, "the demo should expose five differences without tolerance");
        var tolerantDemo = engine.Compare([demoBefore, demoAfter], new ComparisonProfile { AbsoluteTolerance = 0.01 });
        Assert(tolerantDemo.DifferenceCount == 4, "the demo should suppress its small price difference with 0.01 tolerance");
    }

    Console.WriteLine("All comparison engine smoke tests passed.");
}
finally
{
    Directory.Delete(temp, recursive: true);
}

static IEnumerable<CompareNode> Descendants(CompareNode node)
{
    yield return node;
    foreach (var child in node.Children)
        foreach (var descendant in Descendants(child)) yield return descendant;
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException("FAILED: " + message);
}

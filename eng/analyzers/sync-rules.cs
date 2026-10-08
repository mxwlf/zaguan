#:package Microsoft.CodeAnalysis.CSharp@5.0.0
// Regenerates eng/analyzers/all-rules.globalconfig: one explicit entry per rule, for every
// analyzer every project in this repository loads.
//
// WHY A GENERATED FILE RATHER THAN A BULK SEVERITY ENTRY
// -----------------------------------------------------
// Neither `dotnet_analyzer_diagnostic.severity` nor `dotnet_analyzer_diagnostic.category-X.severity`
// switches on a rule its analyzer ships disabled; both only re-rank rules that are already on.
// Verified: RCS1007 stays silent under the global bulk entry and fires under an explicit
// `dotnet_diagnostic.RCS1007.severity` entry. That is also why <AnalysisMode>All</AnalysisMode> is
// implemented by the SDK as 281 explicit CA entries rather than one line. So "every rule on" is
// reachable only by naming every rule, and naming ~1,300 rules by hand is not maintainable.
//
// Usage, from the repository root:
//     dotnet run eng/analyzers/sync-rules.cs             # rewrite the globalconfig
//     dotnet run eng/analyzers/sync-rules.cs --check     # fail if it is stale (for CI)
using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

var repoRoot = FindRepoRoot();
var output = Path.Combine(repoRoot, "eng", "analyzers", "all-rules.globalconfig");
var check = args.Contains("--check");

// Every project, so test-only analyzers (xunit, NSubstitute) are covered too.
var projects = Directory.EnumerateFiles(Path.Combine(repoRoot, "src"), "*.csproj", SearchOption.AllDirectories)
    .Concat(Directory.EnumerateFiles(Path.Combine(repoRoot, "test"), "*.csproj", SearchOption.AllDirectories))
    .OrderBy(p => p, StringComparer.Ordinal)
    .ToArray();

var assemblies = new SortedSet<string>(StringComparer.Ordinal);
foreach (var project in projects)
{
    // ResolvedAnalyzers only - the analyzers that arrive as NuGet packages. ResolvePackageAssets
    // has to run first or it comes back empty.
    //
    // The SDK's own analyzers (@(Analyzer): NetAnalyzers, CodeStyle) are deliberately NOT read, and
    // the reason is determinism rather than taste. This file is committed and `--check` fails the
    // build when it does not match, so everything it is generated from has to be pinned. Package
    // analyzer versions are, by Directory.Packages.props and the lock files. The SDK's are not:
    // global.json pins a version with `rollForward: latestPatch`, so one machine may legitimately
    // resolve a later patch than another, and the NetAnalyzers assembly ships inside the SDK and
    // changes with it. Reflecting over it would make this file a function of the patch level the
    // generating machine happened to have - a disagreement that regenerating moves rather than
    // settles, because the other machine then produces the opposite diff.
    //
    // Nothing is lost by leaving CA out. $(AnalysisMode)=All already configures 281 of the 316 CA
    // ids, and it tracks the SDK automatically because the SDK ships that config beside the
    // analyzer. The 38 it leaves alone are named explicitly in .editorconfig instead - stable text
    // that no SDK bump can invalidate. Generating them here also silently overrode the one rule the
    // SDK deliberately disables, CA1516, by re-enabling it from a second global config.
    foreach (var path in MSBuildItem(project, "ResolvedAnalyzers"))
    {
        assemblies.Add(path);
    }
}

var rules = new Dictionary<string, (DiagnosticSeverity Severity, string Title, string Category, bool OnByDefault)>(StringComparer.Ordinal);
var skipped = new List<string>();

foreach (var path in assemblies)
{
    Assembly assembly;
    try { assembly = Assembly.LoadFrom(path); }
    catch { skipped.Add(Path.GetFileName(path)); continue; }

    Type[] types;
    try { types = assembly.GetTypes(); }
    catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t is not null).ToArray()!; }

    var found = 0;
    foreach (var type in types)
    {
        if (type is null || type.IsAbstract || !typeof(DiagnosticAnalyzer).IsAssignableFrom(type)) continue;
        if (type.GetCustomAttributes<DiagnosticAnalyzerAttribute>().All(a => !a.Languages.Contains(LanguageNames.CSharp))) continue;

        DiagnosticAnalyzer instance;
        try { instance = (DiagnosticAnalyzer)Activator.CreateInstance(type, nonPublic: true)!; }
        catch { continue; }

        foreach (var d in instance.SupportedDiagnostics)
        {
            rules[d.Id] = (d.DefaultSeverity, d.Title.ToString(), d.Category ?? "", d.IsEnabledByDefault);
            found++;
        }
    }

    if (found == 0) skipped.Add(Path.GetFileName(path));
}

// An empty result is always a broken environment, never a real answer: this repository always
// references analyzer packages. Without this guard `--check` reports all 1,065 rules as deleted,
// and - far worse - a plain run WRITES the empty file, silently taking the entire strict bar off
// the build with a change that looks like a routine regeneration in review.
if (projects.Length == 0 || assemblies.Count == 0 || rules.Count == 0)
{
    Console.Error.WriteLine(
        $"Refusing to continue: found {projects.Length} projects, {assemblies.Count} analyzer " +
        $"assemblies and {rules.Count} rules. Expected all three to be non-zero. The usual cause " +
        "is a restore that did not happen or did not complete, leaving no project.assets.json for " +
        "ResolvePackageAssets to read.");
    return 2;
}

var text = Render(rules, projects.Length, assemblies.Count, skipped);

if (check)
{
    var existing = File.Exists(output) ? File.ReadAllText(output) : "";
    if (Normalize(existing) == Normalize(text))
    {
        Console.WriteLine($"all-rules.globalconfig is current ({rules.Count} rules).");
        return 0;
    }

    // Say WHAT differs, not just that something does. A bare "stale" costs a round trip to work
    // out whether a package bump added rules, whether a rule changed severity, or whether only the
    // header counts moved.
    Console.Error.WriteLine("all-rules.globalconfig is stale.");

    var committed = ParseEntries(existing);
    var expected = ParseEntries(text);

    Report("only in the committed file (rule gone from the analyzers)", committed.Keys.Except(expected.Keys));
    Report("missing from the committed file (new rule)", expected.Keys.Except(committed.Keys));
    Report(
        "severity changed",
        committed.Keys.Intersect(expected.Keys)
            .Where(id => committed[id] != expected[id])
            .Select(id => $"{id}: {committed[id]} -> {expected[id]}"));

    if (committed.Count == expected.Count && !committed.Except(expected).Any())
    {
        Console.Error.WriteLine("  the rules are identical; only the header differs.");
    }

    Console.Error.WriteLine("Run: dotnet run eng/analyzers/sync-rules.cs");
    return 1;
}

static string Normalize(string s) => s.Replace("\r\n", "\n");

static Dictionary<string, string> ParseEntries(string content)
{
    var entries = new Dictionary<string, string>(StringComparer.Ordinal);
    foreach (var line in Normalize(content).Split('\n'))
    {
        var trimmed = line.Trim();
        if (!trimmed.StartsWith("dotnet_diagnostic.", StringComparison.Ordinal)) continue;
        var parts = trimmed.Split('=', 2);
        if (parts.Length != 2) continue;
        var id = parts[0].Trim();
        id = id["dotnet_diagnostic.".Length..].Replace(".severity", "", StringComparison.Ordinal);
        entries[id] = parts[1].Trim();
    }
    return entries;
}

static void Report(string label, IEnumerable<string> items)
{
    var list = items.OrderBy(x => x, StringComparer.Ordinal).ToList();
    if (list.Count == 0) return;
    Console.Error.WriteLine($"  {label} ({list.Count}): {string.Join(", ", list.Take(25))}{(list.Count > 25 ? ", ..." : "")}");
}

File.WriteAllText(output, text);
Console.WriteLine($"Wrote {rules.Count} rules from {assemblies.Count} analyzer assemblies across {projects.Length} projects.");
return 0;

static string Render(
    Dictionary<string, (DiagnosticSeverity Severity, string Title, string Category, bool OnByDefault)> rules,
    int projectCount,
    int assemblyCount,
    List<string> skipped)
{
    var byPrefix = rules
        .GroupBy(r => new string(r.Key.TakeWhile(char.IsLetter).ToArray()))
        .OrderBy(g => g.Key, StringComparer.Ordinal);

    var sb = new StringBuilder();
    sb.Append("""
        # GENERATED FILE - DO NOT EDIT BY HAND.
        #
        # Regenerate with, from the repository root:
        #     dotnet run eng/analyzers/sync-rules.cs
        #
        # Every rule of every analyzer this repository resolves from NuGet, each named explicitly,
        # because bulk severity entries do not switch on a rule its analyzer ships disabled - only an
        # explicit per-rule entry does. See the header of sync-rules.cs for the evidence.
        #
        # Package analyzers only. The SDK's own (CA, IDE) are deliberately absent: their versions move
        # with the SDK, which global.json floats, so generating them made this file disagree with
        # itself between machines. CA is handled by $(AnalysisMode) plus an explicit block in
        # .editorconfig; IDE by the category-Style entry there.
        #
        # Severity is `warning`, which $(TreatWarningsAsErrors) turns into an error in Release - so
        # Release and CI hold the strict bar while a Debug build still only warns. Rules whose own
        # default is Error keep `error`, so that nothing here quietly downgrades a rule.
        #
        # THIS FILE IS NOT WHERE EXCEPTIONS GO. A global config loses to .editorconfig for any file
        # .editorconfig matches, so every relaxation belongs in the root .editorconfig (or
        # test/.editorconfig) next to the reason it exists. Editing this file instead means the next
        # regeneration silently reverts it.

        is_global = true

        """);

    sb.Append($"# {rules.Count} rules from {assemblyCount} analyzer assemblies across {projectCount} projects.\n");
    if (skipped.Count > 0)
    {
        sb.Append("# Assemblies contributing no C# rules - source generators, and the code-fix halves of the\n");
        sb.Append("# analyzers above, which carry the fixes rather than the diagnostics:\n");
        foreach (var s in skipped.Distinct().OrderBy(x => x, StringComparer.Ordinal))
        {
            sb.Append($"#   {s}\n");
        }
    }

    foreach (var group in byPrefix)
    {
        var onCount = group.Count(r => r.Value.OnByDefault);
        sb.Append($"\n# {group.Key}: {group.Count()} rules ({group.Count() - onCount} of them shipped disabled by default).\n");
        foreach (var rule in group.OrderBy(r => r.Key, StringComparer.Ordinal))
        {
            var severity = rule.Value.Severity == DiagnosticSeverity.Error ? "error" : "warning";
            sb.Append($"dotnet_diagnostic.{rule.Key}.severity = {severity}\n");
        }
    }

    return sb.ToString();
}

static IEnumerable<string> MSBuildItem(string project, string item)
{
    var psi = new ProcessStartInfo("dotnet")
    {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
    };
    psi.ArgumentList.Add("msbuild");
    psi.ArgumentList.Add(project);
    // -restore matters more than it looks. ResolvePackageAssets reads project.assets.json, which
    // only exists after a restore, and it reports NOTHING rather than failing when the file is
    // absent. On a clean checkout - which is every CI run, and `analyzers-verify` runs before
    // `build` - that produced an empty rule set from a successful-looking command.
    psi.ArgumentList.Add("-restore");
    psi.ArgumentList.Add("-t:ResolvePackageAssets");
    psi.ArgumentList.Add($"-getItem:{item}");
    psi.ArgumentList.Add("-p:Configuration=Release");

    using var process = Process.Start(psi)!;
    var stdout = process.StandardOutput.ReadToEnd();
    process.WaitForExit();
    if (process.ExitCode != 0) yield break;

    JsonDocument document;
    try { document = JsonDocument.Parse(stdout); }
    catch { yield break; }

    using (document)
    {
        if (!document.RootElement.TryGetProperty("Items", out var items)) yield break;
        if (!items.TryGetProperty(item, out var entries)) yield break;
        foreach (var entry in entries.EnumerateArray())
        {
            if (entry.TryGetProperty("FullPath", out var full) && full.GetString() is { } value)
            {
                yield return value;
            }
        }
    }
}

static string FindRepoRoot()
{
    var dir = Directory.GetCurrentDirectory();
    while (dir is not null && !File.Exists(Path.Combine(dir, "Directory.Build.props")))
    {
        dir = Path.GetDirectoryName(dir);
    }
    return dir ?? throw new InvalidOperationException("Could not locate the repository root.");
}

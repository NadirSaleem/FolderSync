using System.Text.Json;
using FolderSync.Engine;

// Usage:
//   sync list                                   - show configured pairs
//   sync preview <pairName>                      - show what would change, apply nothing
//   sync run <pairName>                          - apply changes
//   sync add <name> <leftPath> <rightPath> <mode> - add a pair to pairs.json (Echo|Sync|Contribute)
//
// Pairs are stored in pairs.json under %LocalAppData%\FolderSync, shared with the GUI.

var pairsFilePath = AppPaths.PairsFilePath;
var pairs = LoadPairs(pairsFilePath);
var store = new StateStore();

if (args.Length == 0)
{
    PrintUsage();
    return 1;
}

switch (args[0].ToLowerInvariant())
{
    case "list":
        ListPairs(pairs);
        return 0;

    case "add":
        if (args.Length != 5)
        {
            Console.WriteLine("Usage: sync add <name> <leftPath> <rightPath> <Echo|Sync|Contribute>");
            return 1;
        }
        if (!Enum.TryParse<SyncMode>(args[4], ignoreCase: true, out var mode))
        {
            Console.WriteLine("Mode must be one of: Echo, Sync, Contribute");
            return 1;
        }
        pairs.Add(new FolderPair { Name = args[1], LeftPath = args[2], RightPath = args[3], Mode = mode });
        SavePairs(pairsFilePath, pairs);
        Console.WriteLine($"Added pair '{args[1]}'.");
        return 0;

    case "preview":
    case "run":
        if (args.Length != 2)
        {
            Console.WriteLine($"Usage: sync {args[0]} <pairName>");
            return 1;
        }
        return RunPair(pairs, store, args[1], apply: args[0] == "run");

    default:
        PrintUsage();
        return 1;
}

bool ContentsDiffer(FolderPair pair, string relativePath)
{
    try
    {
        return Scanner.FilesDiffer(pair.LeftPath, pair.RightPath, relativePath);
    }
    catch (Exception ex)
    {
        // Can't prove the files are identical, so don't assume they are — flag it as a
        // conflict rather than risk silently dropping one side's changes.
        Console.WriteLine($"  WARNING: could not hash '{relativePath}' ({ex.Message}); treating as conflict.");
        return true;
    }
}

int RunPair(List<FolderPair> pairList, StateStore stateStore, string name, bool apply)
{
    var pair = pairList.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    if (pair is null)
    {
        Console.WriteLine($"No pair named '{name}'. Run 'sync list' to see configured pairs.");
        return 1;
    }

    var currentLeft = Scanner.Scan(pair.LeftPath, pair);
    var currentRight = Scanner.Scan(pair.RightPath, pair);
    var snapshot = stateStore.Load(pair.Name);

    var plan = DiffEngine.BuildPlan(pair, currentLeft, currentRight, snapshot, relPath => ContentsDiffer(pair, relPath));

    if (plan.Count == 0)
    {
        Console.WriteLine("Already in sync — nothing to do.");
        return 0;
    }

    Console.WriteLine($"{plan.Count} change(s) for pair '{pair.Name}' ({pair.Mode} mode):");
    foreach (var action in plan)
        Console.WriteLine($"  [{action.Type,-16}] {action.RelativePath}  ({action.Reason})");

    var conflictCount = plan.Count(a => a.Type == ActionType.Conflict);
    if (conflictCount > 0)
        Console.WriteLine($"\n{conflictCount} conflict(s) require manual resolution and will be skipped.");

    if (!apply)
    {
        Console.WriteLine("\n(Preview only — nothing was changed. Run 'sync run " + name + "' to apply.)");
        return 0;
    }

    var result = ActionExecutor.Apply(pair, plan, a => Console.WriteLine($"Applying: {a.RelativePath}"));

    Console.WriteLine($"\nDone. Applied {result.Applied}, skipped {result.Skipped}, conflicts {result.Conflicts}.");
    foreach (var error in result.Errors)
        Console.WriteLine($"  ERROR: {error}");

    // Re-scan after applying so the saved snapshot reflects the post-sync reality.
    var newSnapshot = new PairSnapshot
    {
        Left = Scanner.Scan(pair.LeftPath, pair),
        Right = Scanner.Scan(pair.RightPath, pair),
        LastRunUtc = DateTime.UtcNow
    };
    stateStore.Save(pair.Name, newSnapshot);

    return result.Errors.Count > 0 ? 1 : 0;
}

void ListPairs(List<FolderPair> pairList)
{
    if (pairList.Count == 0)
    {
        Console.WriteLine("No pairs configured yet. Use 'sync add' to create one.");
        return;
    }

    foreach (var p in pairList)
        Console.WriteLine($"{p.Name,-20} [{p.Mode,-10}] {p.LeftPath}  <->  {p.RightPath}");
}

List<FolderPair> LoadPairs(string path)
{
    if (!File.Exists(path))
        return new List<FolderPair>();

    var json = File.ReadAllText(path);
    return JsonSerializer.Deserialize<List<FolderPair>>(json) ?? new List<FolderPair>();
}

void SavePairs(string path, List<FolderPair> pairList)
{
    var json = JsonSerializer.Serialize(pairList, new JsonSerializerOptions { WriteIndented = true });
    File.WriteAllText(path, json);
}

void PrintUsage()
{
    Console.WriteLine("""
        FolderSync CLI

        Usage:
          sync list
          sync add <name> <leftPath> <rightPath> <Echo|Sync|Contribute>
          sync preview <pairName>
          sync run <pairName>
        """);
}

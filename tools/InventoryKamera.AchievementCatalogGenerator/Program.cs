using InventoryKamera;
using System;
using System.IO;

if (args.Length != 2)
{
    Console.Error.WriteLine(
        "Usage: dotnet run --project tools/InventoryKamera.AchievementCatalogGenerator -- "
        + "<travelers-toolkit-achievements.json> <achievements-scanner.json>");
    return 2;
}

try
{
    AchievementCatalogGenerationResult result = AchievementCatalogGenerator.Generate(args[0], args[1]);
    Console.WriteLine($"Wrote: {Path.GetFullPath(args[1])}");
    Console.WriteLine(
        $"Achievements: {result.TotalCount} "
        + $"(hidden {result.HiddenCount}, non-hidden {result.NonHiddenCount})");
    Console.WriteLine(
        $"Stages: {result.StagedEntryCount} entries in {result.StageGroupCount} groups");
    Console.WriteLine(
        $"Duplicate names: {result.DuplicateNameCount} "
        + $"({result.StagedDuplicateNameCount} single-stage-group, "
        + $"{result.NonStageDuplicateNames.Count} non-stage/cross-group)");
    foreach (string name in result.NonStageDuplicateNames)
        Console.WriteLine($"Non-stage duplicate: {name}");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}

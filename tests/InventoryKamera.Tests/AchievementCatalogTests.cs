using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using Xunit;

namespace InventoryKamera.Tests
{
    public class AchievementCatalogTests
    {
        [Fact]
        public void PackagedSnapshotLoadsWithAcceptedSchemaAndExpectedCurrentStatistics()
        {
            AchievementCatalog catalog = new AchievementCatalogProvider().Catalog;

            Assert.Equal(1854, catalog.All.Count);
            Assert.Equal(1002, catalog.All.Count(entry => entry.Hidden));
            Assert.Equal(852, catalog.All.Count(entry => !entry.Hidden));
            Assert.Equal(444, catalog.All.Count(entry => entry.IsStaged));
            Assert.Equal(148, catalog.All.Where(entry => entry.StageGroupId.HasValue)
                .Select(entry => entry.StageGroupId.Value).Distinct().Count());
            Assert.All(catalog.All, entry => Assert.True(entry.Primogems >= 0));
        }

        [Fact]
        public void PackagedHiddenTitleGroupsSupportConservativeZeroResultPolicy()
        {
            AchievementCatalog catalog = new AchievementCatalogProvider().Catalog;
            AchievementTitleGroup[] hiddenUnique = catalog.TitleGroups
                .Where(group => group.Kind == AchievementTitleGroupKind.Unique && group.Entries[0].Hidden)
                .ToArray();
            AchievementTitleGroup[] allHiddenStages = catalog.TitleGroups
                .Where(group => group.Kind == AchievementTitleGroupKind.StageFamily &&
                    group.Entries.All(entry => entry.Hidden))
                .ToArray();
            AchievementTitleGroup[] mixed = catalog.TitleGroups
                .Where(group => group.Entries.Any(entry => entry.Hidden) &&
                    group.Entries.Any(entry => !entry.Hidden))
                .ToArray();

            Assert.Equal(995, hiddenUnique.Length);
            Assert.Equal(
                new[] { "The Final Fonta Sea", "Purveyor of Punishment" },
                allHiddenStages.Select(group => group.CanonicalTitle));
            Assert.Equal("Let the Wind Lead", Assert.Single(mixed).CanonicalTitle);

            AchievementCatalogEntry noAirForce = catalog.GetById(81581);
            Assert.Equal("No Air Force", noAirForce.Name);
            Assert.True(noAirForce.Hidden);
            Assert.False(noAirForce.IsStaged);
            Assert.Equal("5.8", noAirForce.Version);
        }

        [Fact]
        public void PackagedLoadingDoesNotDependOnCurrentWorkingDirectory()
        {
            string originalDirectory = Environment.CurrentDirectory;
            string temporaryDirectory = Path.Combine(
                Path.GetTempPath(),
                $"InventoryKamera-AchievementCatalog-{Guid.NewGuid():N}");
            Directory.CreateDirectory(temporaryDirectory);

            try
            {
                Environment.CurrentDirectory = temporaryDirectory;
                Assert.Equal(1854, new AchievementCatalogProvider().Catalog.All.Count);
            }
            finally
            {
                Environment.CurrentDirectory = originalDirectory;
                Directory.Delete(temporaryDirectory);
            }
        }

        [Fact]
        public void EveryCanonicalIdIsUnique()
        {
            AchievementCatalog catalog = LoadPackaged();

            Assert.Equal(catalog.All.Count, catalog.All.Select(entry => entry.Id).Distinct().Count());
        }

        [Fact]
        public void CatalogCollectionsCannotBeMutated()
        {
            AchievementCatalog catalog = LoadPackaged();
            var all = Assert.IsAssignableFrom<IList<AchievementCatalogEntry>>(catalog.All);
            var titleGroups = Assert.IsAssignableFrom<IList<AchievementTitleGroup>>(catalog.TitleGroups);
            AchievementTitleGroup zoo = catalog.GetTitleGroup("Zoo Tycoon");
            var titleEntries = Assert.IsAssignableFrom<IList<AchievementCatalogEntry>>(zoo.Entries);
            var candidateIds = Assert.IsAssignableFrom<IList<int>>(zoo.CandidateIds);
            var exactName = Assert.IsAssignableFrom<IList<AchievementCatalogEntry>>(
                catalog.FindByExactName("Zoo Tycoon"));
            var stageGroup = Assert.IsAssignableFrom<IList<AchievementCatalogEntry>>(
                catalog.GetStageGroup(80127));

            Assert.Throws<NotSupportedException>(() => all.RemoveAt(0));
            Assert.Throws<NotSupportedException>(() => titleGroups.RemoveAt(0));
            Assert.Throws<NotSupportedException>(() => titleEntries.RemoveAt(0));
            Assert.Throws<NotSupportedException>(() => candidateIds.RemoveAt(0));
            Assert.Throws<NotSupportedException>(() => exactName.RemoveAt(0));
            Assert.Throws<NotSupportedException>(() => stageGroup.RemoveAt(0));
        }

        [Fact]
        public void GetByIdReturnsExactCanonicalEntryAndUnknownIdReturnsNull()
        {
            AchievementCatalog catalog = LoadPackaged();

            AchievementCatalogEntry entry = catalog.GetById(80127);

            Assert.NotNull(entry);
            Assert.Equal("Zoo Tycoon", entry.Name);
            Assert.Equal("Use the Omni-Ubiquity Net item to capture 1 wild animal.", entry.Description);
            Assert.Equal(5, entry.Primogems);
            Assert.Null(catalog.GetById(999999));
        }

        [Fact]
        public void ExactNameLookupReturnsOneUniqueTitle()
        {
            AchievementCatalogEntry entry = Assert.Single(
                LoadPackaged().FindByExactName("You There! Fly!"));

            Assert.Equal(81665, entry.Id);
            Assert.Empty(LoadPackaged().FindByExactName("you there! fly!"));
        }

        [Fact]
        public void ZooTycoonLookupReturnsAllStagesInSourceOrder()
        {
            IReadOnlyList<AchievementCatalogEntry> entries =
                LoadPackaged().FindByExactName("Zoo Tycoon");

            Assert.Equal(new[] { 80127, 80128, 80129 }, entries.Select(entry => entry.Id));
            Assert.Equal(new int?[] { 1, 2, 3 }, entries.Select(entry => entry.StageIndex));
            Assert.All(entries, entry => Assert.Equal(80127, entry.StageGroupId));
        }

        [Fact]
        public void NonStageDuplicateNameRemainsExplicitlyAmbiguous()
        {
            IReadOnlyList<AchievementCatalogEntry> entries =
                LoadPackaged().FindByExactName("Let the Wind Lead");

            Assert.Equal(new[] { 84004, 80032 }, entries.Select(entry => entry.Id));
            Assert.All(entries, entry => Assert.False(entry.IsStaged));
        }

        [Fact]
        public void TitleGroupsClassifyUniqueStageFamiliesAndUnrelatedCollision()
        {
            AchievementCatalog catalog = LoadPackaged();

            AchievementTitleGroup unique = catalog.GetTitleGroupForId(81665);
            Assert.Equal(AchievementTitleGroupKind.Unique, unique.Kind);
            Assert.True(unique.IsUnique);
            Assert.Equal(new[] { 81665 }, unique.CandidateIds);

            AchievementTitleGroup zoo = catalog.GetTitleGroup("Zoo Tycoon");
            Assert.Equal(AchievementTitleGroupKind.StageFamily, zoo.Kind);
            Assert.True(zoo.IsStageFamily);
            Assert.Equal(80127, zoo.StageGroupId);
            Assert.Equal(new[] { 80127, 80128, 80129 }, zoo.CandidateIds);

            AchievementTitleGroup moonmist = catalog.GetTitleGroup("Moonmist Treasure Hunter");
            Assert.Equal(AchievementTitleGroupKind.StageFamily, moonmist.Kind);
            Assert.Equal(80490, moonmist.StageGroupId);
            Assert.Equal(new[] { 80490, 80491, 80492 }, moonmist.CandidateIds);

            AchievementTitleGroup collision = catalog.GetTitleGroup("Let the Wind Lead");
            Assert.Equal(AchievementTitleGroupKind.UnrelatedCollision, collision.Kind);
            Assert.True(collision.IsUnrelatedCollision);
            Assert.Null(collision.StageGroupId);
            Assert.Equal(new[] { 80032, 84004 }, collision.CandidateIds);
            Assert.Same(collision, catalog.GetTitleGroupForId(80032));
            Assert.Same(collision, catalog.GetTitleGroupForId(84004));
        }

        [Fact]
        public void TitleGroupCandidateOrderDoesNotChooseCatalogOrderWinner()
        {
            AchievementCatalog catalog = LoadJson(Snapshot(
                Entry(20, "Collision"),
                Entry(10, "Collision")));

            AchievementTitleGroup group = catalog.GetTitleGroup("Collision");

            Assert.Equal(AchievementTitleGroupKind.UnrelatedCollision, group.Kind);
            Assert.Equal(new[] { 10, 20 }, group.CandidateIds);
            Assert.False(group.IsUnique);
        }

        [Fact]
        public void MixedStageMetadataForOneTitleFailsClosedAsUnrelatedCollision()
        {
            AchievementCatalog catalog = LoadJson(Snapshot(
                Entry(1, "Mixed", stageGroupId: 1, stageIndex: 1, stageCount: 2),
                Entry(2, "Mixed", stageGroupId: 1, stageIndex: 2, stageCount: 2),
                Entry(3, "Mixed")));

            AchievementTitleGroup group = catalog.GetTitleGroup("Mixed");

            Assert.Equal(AchievementTitleGroupKind.UnrelatedCollision, group.Kind);
            Assert.Null(group.StageGroupId);
            Assert.Equal(new[] { 1, 2, 3 }, group.CandidateIds);
        }

        [Fact]
        public void CurrentSnapshotHasExpectedTitleGroupBreakdown()
        {
            AchievementCatalog catalog = LoadPackaged();

            Assert.Equal(148, catalog.TitleGroups.Count(group => !group.IsUnique));
            Assert.Equal(147, catalog.TitleGroups.Count(group => group.IsStageFamily));
            AchievementTitleGroup collision = Assert.Single(
                catalog.TitleGroups.Where(group => group.IsUnrelatedCollision));
            Assert.Equal("Let the Wind Lead", collision.CanonicalTitle);
        }

        [Fact]
        public void CurrentStageGroupsAreCompleteAndCatalogSupportsNonThreeStageGroups()
        {
            AchievementCatalog catalog = LoadPackaged();
            int[] groupIds = catalog.All.Where(entry => entry.StageGroupId.HasValue)
                .Select(entry => entry.StageGroupId.Value).Distinct().ToArray();

            Assert.Equal(148, groupIds.Length);
            foreach (int groupId in groupIds)
            {
                IReadOnlyList<AchievementCatalogEntry> group = catalog.GetStageGroup(groupId);
                Assert.Equal(3, group.Count);
                Assert.Equal(new int?[] { 1, 2, 3 }, group.Select(entry => entry.StageIndex));
                Assert.All(group, entry => Assert.Equal(3, entry.StageCount));
            }

            AchievementCatalog twoStage = LoadJson(Snapshot(
                Entry(1, "Future Stage", stageGroupId: 1, stageIndex: 1, stageCount: 2),
                Entry(2, "Future Stage", stageGroupId: 1, stageIndex: 2, stageCount: 2)));
            Assert.Equal(2, twoStage.GetStageGroup(1).Count);
        }

        [Fact]
        public void DuplicateNameAnalysisSeparatesStageGroupsFromOtherCollisions()
        {
            string sourceJson = FindToolkitSourceOrReconstructFromSnapshot();

            AchievementCatalogGenerationResult result =
                AchievementCatalogGenerator.Generate(sourceJson, out _);

            Assert.Equal(148, result.DuplicateNameCount);
            Assert.Equal(147, result.StagedDuplicateNameCount);
            Assert.Equal(new[] { "Let the Wind Lead" }, result.NonStageDuplicateNames);
            Assert.Equal(147, result.RewardResolvableStageGroupCount);
            Assert.Equal(new[] { "The Final Fonta Sea" }, result.RewardAmbiguousStageGroupNames);
        }

        [Fact]
        public void StageRewardsPreserveResolvableAndAmbiguousFamilies()
        {
            AchievementCatalog catalog = LoadPackaged();

            AchievementTitleGroup moonmist = catalog.GetTitleGroup("Moonmist Treasure Hunter");
            Assert.Equal(new[] { 80490, 80491, 80492 }, moonmist.CandidateIds);
            Assert.Equal(new[] { 5, 10, 20 }, moonmist.Entries.Select(entry => entry.Primogems));

            AchievementTitleGroup fonta = catalog.GetTitleGroup("The Final Fonta Sea");
            Assert.Equal(new[] { 81399, 81403, 81404 }, fonta.CandidateIds);
            Assert.Equal(new[] { 5, 5, 5 }, fonta.Entries.Select(entry => entry.Primogems));
        }

        [Fact]
        public void CurrentStageGroupRewardMappingsHaveExpectedBreakdown()
        {
            AchievementCatalog catalog = LoadPackaged();
            var stageGroups = catalog.All
                .Where(entry => entry.StageGroupId.HasValue)
                .GroupBy(entry => entry.StageGroupId.Value)
                .ToArray();
            var ambiguous = stageGroups
                .Where(group => group.Select(entry => entry.Primogems).Distinct().Count() != group.Count())
                .ToArray();

            Assert.Equal(148, stageGroups.Length);
            Assert.Equal(147, stageGroups.Length - ambiguous.Length);
            Assert.Equal("The Final Fonta Sea", Assert.Single(ambiguous).First().Name);
        }

        [Fact]
        public void MissingOrNegativePrimogemRewardIsRejected()
        {
            string missing = Entry(1, "One").Replace("\"primogems\":5,", string.Empty);
            AchievementCatalogException missingError = Assert.Throws<AchievementCatalogException>(() =>
                LoadJson(Snapshot(missing)));
            Assert.Contains("primogems", missingError.Message, StringComparison.OrdinalIgnoreCase);

            string negative = Entry(1, "One").Replace("\"primogems\":5", "\"primogems\":-1");
            AchievementCatalogException negativeError = Assert.Throws<AchievementCatalogException>(() =>
                LoadJson(Snapshot(negative)));
            Assert.Contains("primogems", negativeError.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void GeneratorRejectsMissingOrNegativePrimogemReward()
        {
            string source = SourceEntry("1", "One");
            string missing = source.Replace("\"primogems\":5,", string.Empty);
            string negative = source.Replace("\"primogems\":5", "\"primogems\":-1");

            AchievementCatalogException missingError = Assert.Throws<AchievementCatalogException>(() =>
                AchievementCatalogGenerator.Generate($"[{missing}]", out _));
            AchievementCatalogException negativeError = Assert.Throws<AchievementCatalogException>(() =>
                AchievementCatalogGenerator.Generate($"[{negative}]", out _));

            Assert.Contains("primogems", missingError.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("primogems", negativeError.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void DuplicateIdIsRejected()
        {
            AchievementCatalogException error = Assert.Throws<AchievementCatalogException>(() =>
                LoadJson(Snapshot(Entry(1, "First"), Entry(1, "Second"))));

            Assert.Contains("Duplicate achievement ID", error.Message);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public void EmptyNameIsRejected(string name)
        {
            AchievementCatalogException error = Assert.Throws<AchievementCatalogException>(() =>
                LoadJson(Snapshot(Entry(1, name))));

            Assert.Contains("name", error.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void MissingNameIsRejected()
        {
            string entry = Entry(1, "One").Replace("\"name\":\"One\",", string.Empty);

            AchievementCatalogException error = Assert.Throws<AchievementCatalogException>(() =>
                LoadJson(Snapshot(entry)));

            Assert.Contains("name", error.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void InvalidNumericIdIsRejectedBySnapshotAndSourceLoaders()
        {
            Assert.Throws<AchievementCatalogException>(() =>
                LoadJson(Snapshot(Entry(1, "One").Replace("\"id\":1", "\"id\":\"1\""))));

            string source = SourceEntry("01", "One");
            Assert.Throws<AchievementCatalogException>(() =>
                AchievementCatalogGenerator.Generate($"[{source}]", out _));
        }

        [Fact]
        public void InvalidStageGroupReferenceIsRejected()
        {
            AchievementCatalogException error = Assert.Throws<AchievementCatalogException>(() =>
                LoadJson(Snapshot(
                    Entry(1, "Stage", stageGroupId: 9, stageIndex: 1, stageCount: 2),
                    Entry(2, "Stage", stageGroupId: 9, stageIndex: 2, stageCount: 2))));

            Assert.Contains("does not reference a canonical achievement", error.Message);
        }

        [Fact]
        public void DuplicateStageIndexIsRejected()
        {
            AchievementCatalogException error = Assert.Throws<AchievementCatalogException>(() =>
                LoadJson(Snapshot(
                    Entry(1, "Stage", stageGroupId: 1, stageIndex: 1, stageCount: 2),
                    Entry(2, "Stage", stageGroupId: 1, stageIndex: 1, stageCount: 2))));

            Assert.Contains("duplicate stageIndex", error.Message);
        }

        [Fact]
        public void InconsistentStageCountIsRejected()
        {
            AchievementCatalogException error = Assert.Throws<AchievementCatalogException>(() =>
                LoadJson(Snapshot(
                    Entry(1, "Stage", stageGroupId: 1, stageIndex: 1, stageCount: 2),
                    Entry(2, "Stage", stageGroupId: 1, stageIndex: 2, stageCount: 3))));

            Assert.Contains("inconsistent stageCount", error.Message);
        }

        [Fact]
        public void NonContiguousStagesAreRejected()
        {
            AchievementCatalogException error = Assert.Throws<AchievementCatalogException>(() =>
                LoadJson(Snapshot(
                    Entry(1, "Stage", stageGroupId: 1, stageIndex: 1, stageCount: 3),
                    Entry(2, "Stage", stageGroupId: 1, stageIndex: 3, stageCount: 3))));

            Assert.Contains("contiguous stages", error.Message);
        }

        [Fact]
        public void ImpossibleStageIndexAndCountAreRejected()
        {
            AchievementCatalogException error = Assert.Throws<AchievementCatalogException>(() =>
                LoadJson(Snapshot(
                    Entry(1, "Stage", stageGroupId: 1, stageIndex: 1, stageCount: 2),
                    Entry(2, "Stage", stageGroupId: 1, stageIndex: 3, stageCount: 2))));

            Assert.Contains("impossible stageIndex", error.Message);
        }

        [Theory]
        [InlineData("{\"schemaVersion\":2}")]
        [InlineData("{\"schemaVersion\":2,\"achievements\":null}")]
        public void MissingAchievementsArrayIsRejected(string json)
        {
            AchievementCatalogException error = Assert.Throws<AchievementCatalogException>(() =>
                LoadJson(json));

            Assert.Contains("achievements array", error.Message);
        }

        [Fact]
        public void UnsupportedSchemaIsRejected()
        {
            AchievementCatalogException error = Assert.Throws<AchievementCatalogException>(() =>
                LoadJson("{\"schemaVersion\":1,\"achievements\":[]}"));

            Assert.Contains("Unsupported", error.Message);
        }

        [Fact]
        public void GeneratorProducesIdenticalUtf8BytesAcrossRuns()
        {
            string source = $"[{SourceEntry("1", "One")},{SourceEntry("2", "Two")}]";

            AchievementCatalogGenerator.Generate(source, out string first);
            AchievementCatalogGenerator.Generate(source, out string second);

            Assert.Equal(Encoding.UTF8.GetBytes(first), Encoding.UTF8.GetBytes(second));
            Assert.DoesNotContain('\r', first);
            Assert.EndsWith("\n", first, StringComparison.Ordinal);
        }

        [Fact]
        public void GeneratedToolkitSnapshotMatchesCommittedSnapshot()
        {
            string sourceJson = FindToolkitSourceOrReconstructFromSnapshot();

            AchievementCatalogGenerator.Generate(sourceJson, out string generated);

            Assert.Equal(ReadPackagedSnapshotBytes(), Encoding.UTF8.GetBytes(generated));
        }

        private static AchievementCatalog LoadPackaged() =>
            AchievementCatalogLoader.LoadPackaged();

        private static AchievementCatalog LoadJson(string json)
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
            return AchievementCatalogLoader.Load(stream);
        }

        private static string Snapshot(params string[] entries) =>
            $"{{\"schemaVersion\":2,\"achievements\":[{string.Join(",", entries)}]}}";

        private static string Entry(
            int id,
            string name,
            int? stageGroupId = null,
            int? stageIndex = null,
            int? stageCount = null)
        {
            string stages = stageGroupId.HasValue
                ? $",\"stageGroupId\":{stageGroupId},\"stageIndex\":{stageIndex},\"stageCount\":{stageCount}"
                : string.Empty;
            return $"{{\"id\":{id},\"name\":{Newtonsoft.Json.JsonConvert.SerializeObject(name)},\"description\":\"Description\","
                + $"\"primogems\":5,\"categoryId\":0,\"hidden\":false,\"version\":\"1.0\",\"order\":1{stages}}}";
        }

        private static string SourceEntry(string id, string name) =>
            $"{{\"id\":{Newtonsoft.Json.JsonConvert.SerializeObject(id)},\"categoryId\":\"0\",\"name\":{Newtonsoft.Json.JsonConvert.SerializeObject(name)},"
            + "\"description\":\"Description\",\"primogems\":5,\"hidden\":false,\"version\":\"1.0\",\"order\":1}";

        private static string FindToolkitSourceOrReconstructFromSnapshot()
        {
            string configuredPath = Environment.GetEnvironmentVariable(
                "TRAVELERS_TOOLKIT_ACHIEVEMENTS_JSON");
            if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath))
                return File.ReadAllText(configuredPath, Encoding.UTF8);

            string repositoryRoot = FindRepositoryRoot();
            if (repositoryRoot != null)
            {
                string siblingPath = Path.GetFullPath(Path.Combine(
                    repositoryRoot,
                    "..",
                    "travelers-toolkit",
                    "src",
                    "data",
                    "achievements",
                    "achievements.json"));
                if (File.Exists(siblingPath)) return File.ReadAllText(siblingPath, Encoding.UTF8);
            }

            AchievementCatalog catalog = LoadPackaged();
            var source = new JArray(catalog.All.Select(entry =>
            {
                var item = new JObject
                {
                    ["id"] = entry.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["categoryId"] = entry.CategoryId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["name"] = entry.Name,
                    ["description"] = entry.Description,
                    ["primogems"] = entry.Primogems,
                    ["hidden"] = entry.Hidden,
                    ["version"] = entry.Version,
                    ["order"] = entry.Order,
                };
                if (entry.IsStaged)
                {
                    item["stageGroupId"] = entry.StageGroupId.Value.ToString(
                        System.Globalization.CultureInfo.InvariantCulture);
                    item["stageIndex"] = entry.StageIndex.Value;
                    item["stageCount"] = entry.StageCount.Value;
                }
                return item;
            }));
            return source.ToString(Newtonsoft.Json.Formatting.None);
        }

        private static byte[] ReadPackagedSnapshotBytes()
        {
            using Stream stream = typeof(AchievementCatalogLoader).Assembly.GetManifestResourceStream(
                "InventoryKamera.Resources.Data.achievements-scanner.json")
                ?? throw new FileNotFoundException("Packaged achievement catalog resource was not found.");
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            return memory.ToArray();
        }

        private static string FindRepositoryRoot()
        {
            foreach (string startingPath in new[]
            {
                Environment.CurrentDirectory,
                AppContext.BaseDirectory,
                Path.GetDirectoryName(GetThisSourceFilePath()),
            })
            {
                if (string.IsNullOrWhiteSpace(startingPath)) continue;
                var directory = new DirectoryInfo(startingPath);
                while (directory != null
                    && !File.Exists(Path.Combine(directory.FullName, "InventoryKamera.sln")))
                {
                    directory = directory.Parent;
                }
                if (directory != null) return directory.FullName;
            }

            return null;
        }

        private static string GetThisSourceFilePath([CallerFilePath] string path = null) => path;
    }
}

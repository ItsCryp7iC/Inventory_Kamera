using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace InventoryKamera.Tests
{
    public sealed class AchievementToolkitGoodExportTests
    {
        [Fact]
        public void EmptyGoodExportHasOnlyEmptyNumericArray()
        {
            AchievementToolkitExportResult result = Exporter().ExportGood(Array.Empty<int>());

            Assert.True(result.Success, result.FailureReason);
            Assert.Equal(AchievementToolkitFormat.Good, result.Format);
            Assert.Empty(result.CanonicalIds);
            Assert.Equal("{\n  \"achievements\": []\n}", result.Json);
        }

        [Fact]
        public void OneIdGoodExportUsesANumberNotAString()
        {
            AchievementToolkitExportResult result = Exporter().ExportGood(new[] { 80001 });

            JToken id = Assert.Single((JArray)JObject.Parse(result.Json)["achievements"]);
            Assert.Equal(Newtonsoft.Json.Linq.JTokenType.Integer, id.Type);
            Assert.Equal(80001, id.Value<int>());
        }

        [Fact]
        public void GoodExportDeduplicatesSortsAndIsByteDeterministic()
        {
            AchievementToolkitExporter exporter = Exporter();
            int[] input = { 81665, 80001, 81004, 81665 };

            AchievementToolkitExportResult first = exporter.ExportGood(input);
            AchievementToolkitExportResult second = exporter.ExportGood(input.Reverse());

            Assert.True(first.Success, first.FailureReason);
            Assert.Equal(new[] { 80001, 81004, 81665 }, first.CanonicalIds);
            Assert.Equal(first.Json, second.Json);
            Assert.Equal(
                "{\n" +
                "  \"achievements\": [\n" +
                "    80001,\n" +
                "    81004,\n" +
                "    81665\n" +
                "  ]\n" +
                "}",
                first.Json);

            JArray ids = Assert.IsType<JArray>(JObject.Parse(first.Json)["achievements"]);
            Assert.All(ids, token => Assert.Equal(Newtonsoft.Json.Linq.JTokenType.Integer, token.Type));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(999999999)]
        public void GoodExportRejectsInvalidIdsWithoutJson(int invalidId)
        {
            AchievementToolkitExportResult result = Exporter().ExportGood(
                new[] { 80001, invalidId });

            Assert.False(result.Success);
            Assert.Equal(AchievementExportFailure.InvalidExportIds, result.Failure);
            Assert.Equal(new[] { invalidId }, result.InvalidIds);
            Assert.Null(result.Json);
        }

        [Fact]
        public void GoodExportRejectsMissingInput()
        {
            AchievementToolkitExportResult result = Exporter().ExportGood(null);

            Assert.False(result.Success);
            Assert.Null(result.Json);
            Assert.Contains("No trusted", result.FailureReason);
        }

        [Fact]
        public void GoodExportDiagnosticsAreSummaryOnly()
        {
            AchievementToolkitExportResult result = Exporter().ExportGood(
                new[] { 81665, 80001, 81665 });

            Assert.Contains("operation=achievement-export", result.Diagnostics);
            Assert.Contains("format=Good", result.Diagnostics);
            Assert.Contains("inputIds=3", result.Diagnostics);
            Assert.Contains("canonicalIds=2", result.Diagnostics);
            Assert.Contains("success=True", result.Diagnostics);
            Assert.DoesNotContain("81665", result.Diagnostics);
        }

        [Fact]
        public void ExportResultCollectionsAreReadOnly()
        {
            AchievementToolkitExportResult result = Exporter().ExportGood(new[] { 80001 });

            Assert.Throws<NotSupportedException>(() =>
                Assert.IsAssignableFrom<IList<int>>(result.CanonicalIds).RemoveAt(0));
            Assert.Throws<NotSupportedException>(() =>
                Assert.IsAssignableFrom<IList<int>>(result.InvalidIds).Add(1));
        }

        internal static AchievementToolkitExporter Exporter() =>
            new(new AchievementCatalogProvider());
    }

    public sealed class AchievementToolkitNativeExportTests
    {
        [Fact]
        public void EmptyNativeExportHasToolkitHeaderAndEmptyProgress()
        {
            AchievementToolkitExportResult result = Exporter().ExportNative(Array.Empty<int>());

            Assert.True(result.Success, result.FailureReason);
            Assert.Equal(
                "{\n" +
                "  \"format\": \"TRAVELERS_TOOLKIT_ACHIEVEMENTS\",\n" +
                "  \"version\": 1,\n" +
                "  \"achievementProgress\": {}\n" +
                "}",
                result.Json);
        }

        [Fact]
        public void NativeExportUsesSortedKeysTrueCompletionAndNullTimestamp()
        {
            AchievementToolkitExportResult result = Exporter().ExportNative(
                new[] { 81665, 80001, 81004, 81004 });

            Assert.True(result.Success, result.FailureReason);
            Assert.Equal(
                "{\n" +
                "  \"format\": \"TRAVELERS_TOOLKIT_ACHIEVEMENTS\",\n" +
                "  \"version\": 1,\n" +
                "  \"achievementProgress\": {\n" +
                "    \"80001\": {\n" +
                "      \"completed\": true,\n" +
                "      \"completedAt\": null\n" +
                "    },\n" +
                "    \"81004\": {\n" +
                "      \"completed\": true,\n" +
                "      \"completedAt\": null\n" +
                "    },\n" +
                "    \"81665\": {\n" +
                "      \"completed\": true,\n" +
                "      \"completedAt\": null\n" +
                "    }\n" +
                "  }\n" +
                "}",
                result.Json);

            JObject progress = (JObject)JObject.Parse(result.Json)["achievementProgress"];
            Assert.Equal(new[] { "80001", "81004", "81665" },
                progress.Properties().Select(property => property.Name));
            Assert.All(progress.Properties(), property =>
            {
                Assert.True((bool)property.Value["completed"]);
                Assert.Equal(Newtonsoft.Json.Linq.JTokenType.Null, property.Value["completedAt"].Type);
            });
        }

        [Fact]
        public void NativeExportIsDeterministicAndNeverFabricatesTime()
        {
            AchievementToolkitExporter exporter = Exporter();

            string first = exporter.ExportNative(new[] { 81004, 80001 }).Json;
            string second = exporter.ExportNative(new[] { 80001, 81004 }).Json;

            Assert.Equal(first, second);
            Assert.DoesNotContain("202", first);
            Assert.DoesNotContain("T00:", first);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(999999999)]
        public void NativeExportRejectsInvalidIds(int invalidId)
        {
            AchievementToolkitExportResult result = Exporter().ExportNative(new[] { invalidId });

            Assert.False(result.Success);
            Assert.Equal(AchievementExportFailure.InvalidExportIds, result.Failure);
            Assert.Null(result.Json);
        }

        private static AchievementToolkitExporter Exporter() =>
            AchievementToolkitGoodExportTests.Exporter();
    }

    public sealed class AchievementToolkitGoodImportTests
    {
        [Fact]
        public void GoodImportAcceptsNumericIdsAndCreatesResumeSeed()
        {
            AchievementImportResult result = Importer().Import(
                "{\"achievements\":[81665,80001,81004]}");

            Assert.True(result.ParseSucceeded, result.FailureReason);
            Assert.True(result.SeedCreationSucceeded, result.FailureReason);
            Assert.True(result.Success);
            Assert.Equal(AchievementToolkitFormat.Good, result.Format);
            Assert.Equal(new[] { 80001, 81004, 81665 }, result.ValidIds);
            Assert.Equal(result.ValidIds, result.ResumeSeed.TrustedCompletedIds);
        }

        [Fact]
        public void GoodImportAcceptsNumericStrings()
        {
            AchievementImportResult result = Importer().Import(
                "{\"achievements\":[\"80001\",\"81004\"]}");

            Assert.True(result.Success, result.FailureReason);
            Assert.Equal(new[] { 80001, 81004 }, result.ResumeSeed.TrustedCompletedIds);
        }

        [Fact]
        public void GoodImportTracksDuplicatesWithoutBlockingSeed()
        {
            AchievementImportResult result = Importer().Import(
                "{\"achievements\":[80001,\"80001\",80001]}");

            Assert.True(result.Success, result.FailureReason);
            Assert.Equal(new[] { 80001 }, result.ValidIds);
            Assert.Equal(new[] { "80001" }, result.DuplicateIds);
            Assert.Single(result.ResumeSeed.TrustedCompletedIds);
        }

        [Fact]
        public void GoodUnknownIdsRemainVisibleAndPreventTrustedSeed()
        {
            AchievementImportResult result = Importer().Import(
                "{\"achievements\":[80001,999999]}");

            Assert.True(result.ParseSucceeded);
            Assert.False(result.SeedCreationSucceeded);
            Assert.False(result.Success);
            Assert.Equal(AchievementImportFailure.UnknownCanonicalIds, result.Failure);
            Assert.Equal(new[] { 80001 }, result.ValidIds);
            Assert.Equal(new[] { "999999" }, result.UnknownIds);
            Assert.Null(result.ResumeSeed);
        }

        [Fact]
        public void StrictDigitTextWithLeadingZeroMatchesToolkitAsUnknown()
        {
            AchievementImportResult result = Importer().Import(
                "{\"achievements\":[\"080001\"]}");

            Assert.True(result.ParseSucceeded);
            Assert.Equal(AchievementImportFailure.UnknownCanonicalIds, result.Failure);
            Assert.Equal(new[] { "080001" }, result.UnknownIds);
            Assert.Null(result.ResumeSeed);
        }

        [Fact]
        public void EmptyGoodImportCreatesEmptySeed()
        {
            AchievementImportResult result = Importer().Import("{\"achievements\":[]}");

            Assert.True(result.Success, result.FailureReason);
            Assert.Empty(result.ResumeSeed.TrustedCompletedIds);
        }

        [Theory]
        [InlineData("")]
        [InlineData("null")]
        [InlineData("[]")]
        [InlineData("\"text\"")]
        [InlineData("1")]
        [InlineData("true")]
        [InlineData("{")]
        public void InvalidJsonOrNonObjectRootReturnsStructuredFailure(string json)
        {
            AchievementImportResult result = Importer().Import(json);

            Assert.False(result.ParseSucceeded);
            Assert.False(result.SeedCreationSucceeded);
            Assert.Null(result.ResumeSeed);
            Assert.Equal(AchievementImportFailure.MalformedJson, result.Failure);
        }

        [Fact]
        public void MissingAchievementDataIsUnsupported()
        {
            AchievementImportResult result = Importer().Import(
                "{\"format\":\"GOOD\",\"version\":3}");

            Assert.False(result.ParseSucceeded);
            Assert.Equal(AchievementImportFailure.UnsupportedFormat, result.Failure);
            Assert.Equal(AchievementToolkitFormat.Unsupported, result.Format);
        }

        [Theory]
        [InlineData("{}")]
        [InlineData("\"80001\"")]
        [InlineData("null")]
        public void AchievementsMustBeAnArray(string value)
        {
            AchievementImportResult result = Importer().Import(
                $"{{\"achievements\":{value}}}");

            Assert.False(result.ParseSucceeded);
            Assert.Equal(AchievementImportFailure.MalformedAchievementsArray, result.Failure);
        }

        [Theory]
        [InlineData("null")]
        [InlineData("true")]
        [InlineData("false")]
        [InlineData("{}")]
        [InlineData("[]")]
        [InlineData("1.5")]
        [InlineData("\"1.5\"")]
        [InlineData("\"+80001\"")]
        [InlineData("\"-80001\"")]
        [InlineData("\" 80001 \"")]
        [InlineData("\"not-an-id\"")]
        [InlineData("0")]
        [InlineData("-1")]
        [InlineData("2147483648")]
        [InlineData("\"2147483648\"")]
        public void MalformedGoodEntriesFailWithoutSeed(string entry)
        {
            AchievementImportResult result = Importer().Import(
                $"{{\"achievements\":[{entry}]}}");

            Assert.False(result.ParseSucceeded);
            Assert.Equal(AchievementImportFailure.MalformedAchievementEntry, result.Failure);
            Assert.Null(result.ResumeSeed);
        }

        [Fact]
        public void GoodDetectionWinsWhenFormatIsNotExactNativeMarker()
        {
            AchievementImportResult result = Importer().Import(
                "{\"format\":\"OTHER\",\"achievements\":[80001]}");

            Assert.True(result.Success, result.FailureReason);
            Assert.Equal(AchievementToolkitFormat.Good, result.Format);
        }

        [Fact]
        public void GoodImportDiagnosticsAreBoundedSummaryOnly()
        {
            AchievementImportResult result = Importer().Import(
                "{\"achievements\":[80001,999999,999999]}");

            Assert.Contains("operation=achievement-import", result.Diagnostics);
            Assert.Contains("parseSucceeded=True", result.Diagnostics);
            Assert.Contains("seedReady=False", result.Diagnostics);
            Assert.Contains("validIds=1", result.Diagnostics);
            Assert.Contains("unknownIds=1", result.Diagnostics);
            Assert.Contains("duplicateIds=1", result.Diagnostics);
            Assert.DoesNotContain("999999", result.Diagnostics);
            Assert.True(result.Diagnostics.Length < 800);
        }

        [Fact]
        public void ImportResultCollectionsAreReadOnly()
        {
            AchievementImportResult result = Importer().Import(
                "{\"achievements\":[80001,80001]}");

            Assert.Throws<NotSupportedException>(() =>
                Assert.IsAssignableFrom<IList<int>>(result.ValidIds).RemoveAt(0));
            Assert.Throws<NotSupportedException>(() =>
                Assert.IsAssignableFrom<IList<string>>(result.UnknownIds).Add("1"));
            Assert.Throws<NotSupportedException>(() =>
                Assert.IsAssignableFrom<IList<string>>(result.DuplicateIds).RemoveAt(0));
        }

        internal static AchievementToolkitImporter Importer() =>
            new(new AchievementCatalogProvider());
    }

    public sealed class AchievementToolkitNativeImportTests
    {
        [Fact]
        public void ValidNativeImportAcceptsNullAndCanonicalTimestamp()
        {
            AchievementImportResult result = Importer().Import(
                "{\"format\":\"TRAVELERS_TOOLKIT_ACHIEVEMENTS\",\"version\":1," +
                "\"achievementProgress\":{" +
                "\"80001\":{\"completed\":true,\"completedAt\":null}," +
                "\"81004\":{\"completed\":true,\"completedAt\":\"2026-10-01T00:00:00.000Z\"}}}");

            Assert.True(result.Success, result.FailureReason);
            Assert.Equal(AchievementToolkitFormat.TravelersToolkitNative, result.Format);
            Assert.Equal(new[] { 80001, 81004 }, result.ResumeSeed.TrustedCompletedIds);
        }

        [Fact]
        public void EmptyNativeImportCreatesEmptySeed()
        {
            AchievementImportResult result = Importer().Import(Native("{}"));

            Assert.True(result.Success, result.FailureReason);
            Assert.Empty(result.ResumeSeed.TrustedCompletedIds);
        }

        [Fact]
        public void NativeMarkerMustMatchExactly()
        {
            AchievementImportResult result = Importer().Import(
                "{\"format\":\"travelers_toolkit_achievements\",\"version\":1," +
                "\"achievementProgress\":{}}");

            Assert.False(result.ParseSucceeded);
            Assert.Equal(AchievementImportFailure.UnsupportedFormat, result.Failure);
        }

        [Fact]
        public void OnlyExactBooleanTrueCreatesCompletion()
        {
            AchievementImportResult result = Importer().Import(Native(
                "{\"80001\":{\"completed\":false,\"completedAt\":\"bad\"}," +
                "\"81004\":{\"completed\":\"true\",\"completedAt\":\"bad\"}," +
                "\"81665\":{}}"));

            Assert.True(result.Success, result.FailureReason);
            Assert.Empty(result.ValidIds);
        }

        [Theory]
        [InlineData("2")]
        [InlineData("0")]
        [InlineData("\"1\"")]
        [InlineData("null")]
        public void UnsupportedOrMalformedVersionIsRejected(string version)
        {
            AchievementImportResult result = Importer().Import(
                $"{{\"format\":\"TRAVELERS_TOOLKIT_ACHIEVEMENTS\",\"version\":{version},\"achievementProgress\":{{}}}}");

            Assert.False(result.ParseSucceeded);
            Assert.Equal(AchievementImportFailure.UnsupportedVersion, result.Failure);
        }

        [Theory]
        [InlineData("")]
        [InlineData(",\"achievementProgress\":[]")]
        [InlineData(",\"achievementProgress\":null")]
        public void NativeProgressMustBeAnObject(string progressProperty)
        {
            AchievementImportResult result = Importer().Import(
                "{\"format\":\"TRAVELERS_TOOLKIT_ACHIEVEMENTS\",\"version\":1" +
                progressProperty + "}");

            Assert.False(result.ParseSucceeded);
            Assert.Equal(AchievementImportFailure.MalformedNativeProgress, result.Failure);
        }

        [Theory]
        [InlineData("null")]
        [InlineData("[]")]
        [InlineData("\"entry\"")]
        [InlineData("true")]
        public void NativeProgressEntryMustBeAnObject(string entry)
        {
            AchievementImportResult result = Importer().Import(
                Native($"{{\"80001\":{entry}}}"));

            Assert.False(result.ParseSucceeded);
            Assert.Equal(AchievementImportFailure.MalformedNativeProgress, result.Failure);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("\"2026-10-01\"")]
        [InlineData("\"2026-10-01T00:00:00Z\"")]
        [InlineData("\"2026-10-01T00:00:00.000+00:00\"")]
        [InlineData("\"2026-02-30T00:00:00.000Z\"")]
        [InlineData("\"yesterday\"")]
        [InlineData("123")]
        public void CompletedNativeEntryRequiresNullOrExactCanonicalTimestamp(string completedAt)
        {
            string property = completedAt == null
                ? string.Empty
                : $",\"completedAt\":{completedAt}";
            AchievementImportResult result = Importer().Import(
                Native($"{{\"80001\":{{\"completed\":true{property}}}}}"));

            Assert.False(result.ParseSucceeded);
            Assert.Equal(AchievementImportFailure.InvalidCompletedAt, result.Failure);
            Assert.Null(result.ResumeSeed);
        }

        [Fact]
        public void NativeUnknownIdsPreventSeedButPreserveKnownIds()
        {
            AchievementImportResult result = Importer().Import(Native(
                "{\"80001\":{\"completed\":true,\"completedAt\":null}," +
                "\"999999\":{\"completed\":true,\"completedAt\":null}}"));

            Assert.True(result.ParseSucceeded);
            Assert.False(result.SeedCreationSucceeded);
            Assert.Equal(new[] { 80001 }, result.ValidIds);
            Assert.Equal(new[] { "999999" }, result.UnknownIds);
            Assert.Equal(AchievementImportFailure.UnknownCanonicalIds, result.Failure);
        }

        [Fact]
        public void PrototypeLikeKeysAreIgnoredBeforeEntryValidation()
        {
            AchievementImportResult result = Importer().Import(Native(
                "{\"__proto__\":null,\"constructor\":[],\"prototype\":\"ignored\"," +
                "\"80001\":{\"completed\":true,\"completedAt\":null}}"));

            Assert.True(result.Success, result.FailureReason);
            Assert.Equal(new[] { 80001 }, result.ValidIds);
        }

        [Theory]
        [InlineData("+80001")]
        [InlineData("-80001")]
        [InlineData("80001.0")]
        [InlineData("2147483648")]
        public void CompletedNativeKeysMustBeBoundedPositiveDecimalText(string key)
        {
            AchievementImportResult result = Importer().Import(Native(
                $"{{\"{key}\":{{\"completed\":true,\"completedAt\":null}}}}"));

            Assert.False(result.ParseSucceeded);
            Assert.Equal(AchievementImportFailure.MalformedNativeProgress, result.Failure);
        }

        [Fact]
        public void NativeLeadingZeroKeyIsUnknownLikeToolkitCatalogLookup()
        {
            AchievementImportResult result = Importer().Import(Native(
                "{\"080001\":{\"completed\":true,\"completedAt\":null}}"));

            Assert.True(result.ParseSucceeded);
            Assert.Equal(new[] { "080001" }, result.UnknownIds);
            Assert.Null(result.ResumeSeed);
        }

        private static AchievementToolkitImporter Importer() =>
            AchievementToolkitGoodImportTests.Importer();

        private static string Native(string progress) =>
            "{\"format\":\"TRAVELERS_TOOLKIT_ACHIEVEMENTS\",\"version\":1," +
            $"\"achievementProgress\":{progress}}}";
    }

    public sealed class AchievementToolkitRoundTripTests
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ExportImportRoundTripPreservesCanonicalTrustedSet(bool native)
        {
            int[] trusted = { 81665, 80001, 81004 };
            AchievementToolkitExporter exporter = AchievementToolkitGoodExportTests.Exporter();
            AchievementToolkitExportResult exported = native
                ? exporter.ExportNative(trusted)
                : exporter.ExportGood(trusted);

            AchievementImportResult imported = AchievementToolkitGoodImportTests
                .Importer()
                .Import(exported.Json);

            Assert.True(exported.Success, exported.FailureReason);
            Assert.True(imported.Success, imported.FailureReason);
            Assert.Equal(new[] { 80001, 81004, 81665 },
                imported.ResumeSeed.TrustedCompletedIds);
        }

        [Fact]
        public void ImportedSeedCanBeUsedDirectlyByPhase6IPlanning()
        {
            AchievementCatalog catalog = new AchievementCatalogProvider().Catalog;
            AchievementImportResult imported = AchievementToolkitGoodImportTests
                .Importer()
                .Import("{\"achievements\":[81665]}");

            bool succeeded = AchievementUpdatePlan.TryCreate(
                catalog,
                imported.ResumeSeed,
                out AchievementUpdatePlan plan,
                out IReadOnlyList<int> invalidIds,
                out string failure);

            Assert.True(succeeded, failure);
            Assert.Empty(invalidIds);
            AchievementSkippedTarget skipped = Assert.Single(plan.SkippedTargets);
            Assert.Equal("You There! Fly!", skipped.CanonicalTitle);
        }

        [Fact]
        public void ResolvedStagedCompletionsRoundTripThroughToolkitGoodExport()
        {
            AchievementCatalog catalog = new AchievementCatalogProvider().Catalog;
            var stageProgress = new AchievementStageProgressResult(
                AchievementStageProgressState.Resolved,
                80490,
                3,
                80492,
                new[] { 80490, 80491 },
                rewardResult: null,
                reason: "test resolved progress",
                diagnostics: "stage diagnostics");
            var scan = new AchievementSingleScanResult(
                AchievementSingleScanState.Incomplete,
                80490,
                "Moonmist Treasure Hunter",
                catalog.GetTitleGroup("Moonmist Treasure Hunter"),
                null, null, null, null, null,
                exitAttempted: true,
                exitSucceeded: true,
                finalScreenState: AchievementScreenState.Idle,
                pendingClaim: false,
                reason: "test scan",
                diagnostics: "scan diagnostics",
                stageProgressResult: stageProgress);
            IReadOnlyList<int> trusted = AchievementTrustedCompletionExtractor.Extract(new[] { scan });

            AchievementToolkitExportResult exported = AchievementToolkitGoodExportTests
                .Exporter()
                .ExportGood(trusted);
            AchievementImportResult imported = AchievementToolkitGoodImportTests
                .Importer()
                .Import(exported.Json);

            Assert.True(exported.Success, exported.FailureReason);
            Assert.True(imported.Success, imported.FailureReason);
            Assert.Equal(new[] { 80490, 80491 }, imported.ResumeSeed.TrustedCompletedIds);
            Assert.DoesNotContain(80492, imported.ResumeSeed.TrustedCompletedIds);
        }
    }

    public sealed class AchievementTrustedCompletionExtractorTests
    {
        [Fact]
        public void ExtractIncludesOnlyUniqueCompletedResult()
        {
            AchievementCatalog catalog = new AchievementCatalogProvider().Catalog;
            AchievementSingleScanResult[] results =
            {
                Result(catalog, 81665, AchievementSingleScanState.Completed),
                Result(catalog, 80127, AchievementSingleScanState.Completed),
                Result(catalog, 84004, AchievementSingleScanState.Completed),
                Result(catalog, 81004, AchievementSingleScanState.Incomplete),
            };

            IReadOnlyList<int> trusted = AchievementTrustedCompletionExtractor.Extract(results);

            Assert.Equal(new[] { 81665 }, trusted);
        }

        [Theory]
        [InlineData((int)AchievementSingleScanState.Incomplete)]
        [InlineData((int)AchievementSingleScanState.Unresolved)]
        [InlineData((int)AchievementSingleScanState.AmbiguousCollision)]
        [InlineData((int)AchievementSingleScanState.AmbiguousMultiMatch)]
        [InlineData((int)AchievementSingleScanState.PendingClaimRequiresUserAction)]
        [InlineData((int)AchievementSingleScanState.Failed)]
        [InlineData((int)AchievementSingleScanState.Cancelled)]
        public void NonCompletedUniqueStatesAreExcluded(int stateValue)
        {
            AchievementCatalog catalog = new AchievementCatalogProvider().Catalog;

            IReadOnlyList<int> trusted = AchievementTrustedCompletionExtractor.Extract(
                new[]
                {
                    Result(catalog, 81665, (AchievementSingleScanState)stateValue),
                });

            Assert.Empty(trusted);
        }

        [Fact]
        public void MergePreservesPreviousIdsAndAddsOnlySafeUniqueCompletion()
        {
            AchievementCatalog catalog = new AchievementCatalogProvider().Catalog;

            IReadOnlyList<int> trusted = AchievementTrustedCompletionExtractor.Merge(
                new[] { 81004, 80001, 81004 },
                new[]
                {
                    Result(catalog, 81665, AchievementSingleScanState.Completed),
                    Result(catalog, 80127, AchievementSingleScanState.PendingClaimRequiresUserAction),
                    Result(catalog, 84004, AchievementSingleScanState.AmbiguousCollision),
                });

            Assert.Equal(new[] { 80001, 81004, 81665 }, trusted);
            Assert.Throws<NotSupportedException>(() =>
                Assert.IsAssignableFrom<IList<int>>(trusted).RemoveAt(0));
        }

        [Fact]
        public void ResolvedStageProgressAddsOnlyResolverProvidedCompletedIds()
        {
            AchievementCatalog catalog = new AchievementCatalogProvider().Catalog;
            AchievementStageProgressResult stageProgress = StageProgress(
                AchievementStageProgressState.Resolved,
                80490,
                3,
                80492,
                new[] { 80490, 80491 });

            IReadOnlyList<int> trusted = AchievementTrustedCompletionExtractor.Merge(
                new[] { 80001 },
                new[]
                {
                    Result(
                        catalog,
                        80490,
                        AchievementSingleScanState.Incomplete,
                        stageProgress),
                });

            Assert.Equal(new[] { 80001, 80490, 80491 }, trusted);
            Assert.DoesNotContain(80492, trusted);
        }

        [Theory]
        [InlineData((int)AchievementStageProgressState.PendingClaim)]
        [InlineData((int)AchievementStageProgressState.AmbiguousReward)]
        [InlineData((int)AchievementStageProgressState.UnresolvedReward)]
        [InlineData((int)AchievementStageProgressState.InvalidContext)]
        public void NonResolvedStageProgressAddsNothing(int stateValue)
        {
            AchievementCatalog catalog = new AchievementCatalogProvider().Catalog;
            AchievementStageProgressResult stageProgress = StageProgress(
                (AchievementStageProgressState)stateValue,
                80490,
                null,
                null,
                Array.Empty<int>());

            IReadOnlyList<int> trusted = AchievementTrustedCompletionExtractor.Extract(
                new[]
                {
                    Result(
                        catalog,
                        80490,
                        AchievementSingleScanState.Incomplete,
                        stageProgress),
                });

            Assert.Empty(trusted);
        }

        private static AchievementSingleScanResult Result(
            AchievementCatalog catalog,
            int requestedId,
            AchievementSingleScanState state,
            AchievementStageProgressResult stageProgressResult = null)
        {
            AchievementCatalogEntry entry = catalog.GetById(requestedId);
            return new AchievementSingleScanResult(
                state,
                requestedId,
                entry.Name,
                catalog.GetTitleGroupForId(requestedId),
                startDetection: null,
                searchResult: null,
                identityResult: null,
                completionResult: null,
                exitResult: null,
                exitAttempted: true,
                exitSucceeded: true,
                finalScreenState: AchievementScreenState.Idle,
                pendingClaim: state == AchievementSingleScanState.PendingClaimRequiresUserAction,
                reason: "test",
                diagnostics: "test diagnostics",
                stageProgressResult: stageProgressResult);
        }

        private static AchievementStageProgressResult StageProgress(
            AchievementStageProgressState state,
            int stageGroupId,
            int? currentStageIndex,
            int? currentStageId,
            int[] completedIds) =>
            new(
                state,
                stageGroupId,
                currentStageIndex,
                currentStageId,
                completedIds,
                rewardResult: null,
                reason: "test stage progress",
                diagnostics: "stage diagnostics");
    }
}

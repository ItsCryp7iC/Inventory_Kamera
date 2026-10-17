using System;
using System.Collections.Concurrent;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace InventoryKamera.Tests
{
	public class FailureDiagnosticStoreTests
	{
		[Fact]
		public void CreatesUniqueRunDirectoriesAndFailureDirectories()
		{
			using var temp = new TemporaryDirectory();
			var first = Store(temp.Path, "20260101_000001_000_aaaaaaaa");
			var second = Store(temp.Path, "20260101_000002_000_bbbbbbbb");

			FailureDiagnosticScope firstFailure = first.CreateFailure("talent_verification", "Jean");
			FailureDiagnosticScope secondFailure = first.CreateFailure("talent_verification", "Jean");
			FailureDiagnosticScope otherRunFailure = second.CreateFailure("talent_verification", "Jean");

			Assert.NotEqual(first.RunDirectory, second.RunDirectory);
			Assert.NotEqual(firstFailure.DirectoryPath, secondFailure.DirectoryPath);
			Assert.NotEqual(firstFailure.DirectoryPath, otherRunFailure.DirectoryPath);
			Assert.All(
				new[] { firstFailure.DirectoryPath, secondFailure.DirectoryPath, otherRunFailure.DirectoryPath },
				path => Assert.True(Directory.Exists(path)));
		}

		[Fact]
		public void ConcurrentFailuresDoNotCollide()
		{
			using var temp = new TemporaryDirectory();
			var store = Store(temp.Path, "20260101_000001_000_aaaaaaaa");
			var paths = new ConcurrentBag<string>();

			Parallel.For(0, 20, _ => paths.Add(
				store.CreateFailure("character_verification", "Jean").DirectoryPath));

			Assert.Equal(20, paths.Distinct(StringComparer.OrdinalIgnoreCase).Count());
		}

		[Fact]
		public void ScopeSavesWithoutTakingBitmapOwnership()
		{
			using var temp = new TemporaryDirectory();
			var store = Store(temp.Path, "20260101_000001_000_aaaaaaaa");
			FailureDiagnosticScope failure = store.CreateFailure("destination_verification", "inventory");
			using var bitmap = new Bitmap(2, 2);

			string saved = failure.SaveBitmap("failure_full.png", bitmap);

			Assert.True(File.Exists(saved));
			bitmap.SetPixel(0, 0, Color.Red);
			Assert.Equal(Color.Red.ToArgb(), bitmap.GetPixel(0, 0).ToArgb());
		}

		[Fact]
		public void RetainsNewestConfiguredRunsAndDeletesOlderRuns()
		{
			using var temp = new TemporaryDirectory();
			string root = Path.Combine(temp.Path, "failures");
			string[] ids = Enumerable.Range(1, 5).Select(RunId).ToArray();
			foreach (string id in ids) CreateRun(root, id);
			var store = new FailureDiagnosticStore(root, "20260102_000001_000_ffffffff", retainedRunCount: 3);

			Assert.True(store.ApplyRetention());

			Assert.Equal(ids.Skip(2).OrderBy(id => id), ExistingRunIds(root));
			Assert.False(Directory.Exists(Path.Combine(root, ids[0])));
			Assert.False(Directory.Exists(Path.Combine(root, ids[1])));
		}

		[Fact]
		public void ActiveRunIsNeverDeletedEvenWhenItIsOldest()
		{
			using var temp = new TemporaryDirectory();
			string root = Path.Combine(temp.Path, "failures");
			string activeId = RunId(1);
			foreach (int value in Enumerable.Range(1, 5)) CreateRun(root, RunId(value));
			var store = new FailureDiagnosticStore(root, activeId, retainedRunCount: 3);

			Assert.True(store.ApplyRetention());

			Assert.True(Directory.Exists(Path.Combine(root, activeId)));
			Assert.Equal(
				new[] { activeId, RunId(4), RunId(5) }.OrderBy(id => id),
				ExistingRunIds(root));
		}

		[Fact]
		public void FirstFailureKeepsTotalRunCountWithinLimit()
		{
			using var temp = new TemporaryDirectory();
			string root = Path.Combine(temp.Path, "failures");
			foreach (int value in Enumerable.Range(1, 3)) CreateRun(root, RunId(value));
			string activeId = RunId(4);
			var store = new FailureDiagnosticStore(root, activeId, retainedRunCount: 3);

			store.CreateFailure("character_verification", "Jean");

			Assert.Equal(
				new[] { RunId(2), RunId(3), activeId },
				ExistingRunIds(root));
		}

		[Fact]
		public void CleanupBelowLimitDoesNothing()
		{
			using var temp = new TemporaryDirectory();
			string root = Path.Combine(temp.Path, "failures");
			CreateRun(root, RunId(1));
			CreateRun(root, RunId(2));
			var store = new FailureDiagnosticStore(root, RunId(3), retainedRunCount: 10);

			Assert.True(store.ApplyRetention());

			Assert.Equal(new[] { RunId(1), RunId(2) }, ExistingRunIds(root));
		}

		[Fact]
		public void MalformedContentIsIgnoredAndCleanupStaysInsideRoot()
		{
			using var temp = new TemporaryDirectory();
			string root = Path.Combine(temp.Path, "logging", "failures");
			string outside = Path.Combine(temp.Path, "exports");
			Directory.CreateDirectory(outside);
			File.WriteAllText(Path.Combine(outside, "GOOD.json"), "keep");
			Directory.CreateDirectory(Path.Combine(root, "legacy_character_verification"));
			File.WriteAllText(Path.Combine(root, "README.txt"), "keep");
			CreateRun(root, RunId(1));
			CreateRun(root, RunId(2));
			var store = new FailureDiagnosticStore(root, RunId(3), retainedRunCount: 1);

			Assert.True(store.ApplyRetention());

			Assert.True(Directory.Exists(Path.Combine(root, "legacy_character_verification")));
			Assert.True(File.Exists(Path.Combine(root, "README.txt")));
			Assert.True(File.Exists(Path.Combine(outside, "GOOD.json")));
		}

		[Fact]
		public void CleanupExceptionIsNonFatal()
		{
			using var temp = new TemporaryDirectory();
			string root = Path.Combine(temp.Path, "failures");
			CreateRun(root, RunId(1));
			CreateRun(root, RunId(2));
			var store = new FailureDiagnosticStore(
				root,
				RunId(3),
				retainedRunCount: 1,
				deleteRunDirectory: _ => throw new IOException("simulated cleanup failure"));

			bool result = store.ApplyRetention();

			Assert.False(result);
			Assert.True(Directory.Exists(Path.Combine(root, RunId(1))));
			Assert.True(Directory.Exists(Path.Combine(root, RunId(2))));
		}

		[Fact]
		public void TransientScreenshotResetDoesNotTouchFailuresOrOtherFiles()
		{
			using var temp = new TemporaryDirectory();
			string loggingRoot = Path.Combine(temp.Path, "logging");
			string[] transient = { "weapons", "artifacts", "characters", "materials" };
			foreach (string name in transient)
			{
				string directory = Path.Combine(loggingRoot, name);
				Directory.CreateDirectory(directory);
				File.WriteAllText(Path.Combine(directory, "old.png"), "old");
			}
			string failures = Path.Combine(loggingRoot, "failures", RunId(1));
			Directory.CreateDirectory(failures);
			File.WriteAllText(Path.Combine(failures, "evidence.png"), "keep");
			File.WriteAllText(Path.Combine(loggingRoot, "InventoryKamera.debug.log"), "keep");

			GameScanner.ResetTransientLogging(loggingRoot);

			Assert.All(transient, name =>
			{
				string directory = Path.Combine(loggingRoot, name);
				Assert.True(Directory.Exists(directory));
				Assert.Empty(Directory.EnumerateFileSystemEntries(directory));
			});
			Assert.True(File.Exists(Path.Combine(failures, "evidence.png")));
			Assert.True(File.Exists(Path.Combine(loggingRoot, "InventoryKamera.debug.log")));
		}

		private static FailureDiagnosticStore Store(string tempRoot, string runId) =>
			new FailureDiagnosticStore(Path.Combine(tempRoot, "failures"), runId);

		private static string RunId(int second) =>
			$"20260101_0000{second:00}_000_{second:x8}";

		private static void CreateRun(string root, string runId)
		{
			string directory = Path.Combine(root, runId);
			Directory.CreateDirectory(directory);
			File.WriteAllText(Path.Combine(directory, "evidence.txt"), runId);
		}

		private static string[] ExistingRunIds(string root) =>
			Directory.GetDirectories(root)
				.Select(Path.GetFileName)
				.Where(name => name.StartsWith("2026", StringComparison.Ordinal))
				.OrderBy(name => name)
				.ToArray();

		private sealed class TemporaryDirectory : IDisposable
		{
			internal TemporaryDirectory()
			{
				Path = System.IO.Path.Combine(
					System.IO.Path.GetTempPath(),
					"InventoryKamera.Tests",
					Guid.NewGuid().ToString("N"));
				Directory.CreateDirectory(Path);
			}

			internal string Path { get; }

			public void Dispose()
			{
				if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
			}
		}
	}
}

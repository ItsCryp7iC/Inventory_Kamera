using NLog;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;

namespace InventoryKamera
{
	/// <summary>
	/// Owns failure evidence for one scan run. New evidence is written beneath one unique run
	/// directory; retention only considers sibling run directories created by this type.
	/// </summary>
	internal sealed class FailureDiagnosticStore
	{
		internal const int DefaultRetainedRunCount = 10;

		private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
		private static readonly Regex RunIdPattern = new Regex(
			@"^\d{8}_\d{6}_\d{3}_[0-9a-f]{8}$",
			RegexOptions.Compiled | RegexOptions.CultureInvariant);

		private readonly string rootDirectory;
		private readonly string runDirectory;
		private readonly int retainedRunCount;
		private readonly Action<string> deleteRunDirectory;
		private int failureSequence;
		private int activeRunRetentionApplied;

		internal FailureDiagnosticStore(
			string rootDirectory,
			string runId = null,
			int retainedRunCount = DefaultRetainedRunCount,
			Action<string> deleteRunDirectory = null)
		{
			if (string.IsNullOrWhiteSpace(rootDirectory))
				throw new ArgumentException("A diagnostics root is required.", nameof(rootDirectory));
			if (retainedRunCount < 1)
				throw new ArgumentOutOfRangeException(nameof(retainedRunCount));

			RunId = runId ?? CreateRunId();
			if (!RunIdPattern.IsMatch(RunId))
				throw new ArgumentException("The diagnostic run identifier is not valid.", nameof(runId));

			this.rootDirectory = Path.GetFullPath(rootDirectory);
			runDirectory = Path.Combine(this.rootDirectory, RunId);
			this.retainedRunCount = retainedRunCount;
			this.deleteRunDirectory = deleteRunDirectory ??
				(path => Directory.Delete(path, recursive: true));
		}

		internal static FailureDiagnosticStore CreateDefault() =>
			new FailureDiagnosticStore(Path.Combine(".", "logging", "failures"));

		internal string RunId { get; }
		internal string RootDirectory => rootDirectory;
		internal string RunDirectory => runDirectory;

		/// <summary>
		/// Creates a uniquely named failure directory. The returned scope owns no images: callers
		/// retain ownership and may dispose them immediately after <see cref="FailureDiagnosticScope.SaveBitmap"/>.
		/// </summary>
		internal FailureDiagnosticScope CreateFailure(string category, string context)
		{
			string safeCategory = SafeSegment(category, "unknown");
			string safeContext = SafeSegment(context, "failure");
			int sequence = Interlocked.Increment(ref failureSequence);
			string directory = Path.Combine(
				runDirectory,
				safeCategory,
				$"{sequence:0000}_{safeContext}");
			EnsureDescendant(directory, runDirectory);
			Directory.CreateDirectory(directory);
			// Scan-start cleanup bounds existing runs. Re-apply once, after the active run first
			// materializes, so adding this run cannot temporarily push the retained set past its cap.
			if (Interlocked.Exchange(ref activeRunRetentionApplied, 1) == 0)
				ApplyRetention();
			return new FailureDiagnosticScope(directory);
		}

		/// <summary>
		/// Retains the active run plus the newest prior runs up to the configured limit. Unknown
		/// files/directories and legacy diagnostic layouts are intentionally ignored.
		/// Failure is non-fatal and is reported to the normal application log.
		/// </summary>
		internal bool ApplyRetention()
		{
			try
			{
				if (!Directory.Exists(rootDirectory)) return true;

				List<DirectoryInfo> runs = new DirectoryInfo(rootDirectory)
					.EnumerateDirectories()
					.Where(directory =>
						RunIdPattern.IsMatch(directory.Name) &&
						(directory.Attributes & FileAttributes.ReparsePoint) == 0)
					.OrderByDescending(directory => directory.Name, StringComparer.Ordinal)
					.ToList();

				DirectoryInfo active = runs.FirstOrDefault(directory =>
					string.Equals(directory.Name, RunId, StringComparison.Ordinal));
				var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				if (active != null) keep.Add(active.FullName);

				int priorSlots = retainedRunCount - (active == null ? 0 : 1);
				foreach (DirectoryInfo directory in runs
					.Where(directory => active == null ||
						!string.Equals(directory.FullName, active.FullName, StringComparison.OrdinalIgnoreCase))
					.Take(priorSlots))
				{
					keep.Add(directory.FullName);
				}

				bool succeeded = true;
				foreach (DirectoryInfo directory in runs.Where(directory => !keep.Contains(directory.FullName)))
				{
					try
					{
						EnsureDirectChild(directory.FullName);
						deleteRunDirectory(directory.FullName);
					}
					catch (Exception ex)
					{
						succeeded = false;
						Logger.Warn(ex, "Could not remove expired diagnostic run {0}.", directory.FullName);
					}
				}

				return succeeded;
			}
			catch (Exception ex)
			{
				Logger.Warn(ex, "Could not apply failure-diagnostic retention under {0}.", rootDirectory);
				return false;
			}
		}

		private static string CreateRunId() =>
			DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture) + "_" +
			Guid.NewGuid().ToString("N").Substring(0, 8);

		private static string SafeSegment(string value, string fallback)
		{
			string safe = Regex.Replace(value ?? string.Empty, @"[^a-zA-Z0-9_-]", "_").Trim('_');
			return string.IsNullOrWhiteSpace(safe) ? fallback : safe;
		}

		private void EnsureDirectChild(string path)
		{
			string parent = Path.GetDirectoryName(Path.GetFullPath(path));
			if (!string.Equals(parent, rootDirectory, StringComparison.OrdinalIgnoreCase))
				throw new InvalidOperationException("Diagnostic retention target escaped its root directory.");
		}

		private static void EnsureDescendant(string path, string root)
		{
			string fullPath = Path.GetFullPath(path);
			string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) +
				Path.DirectorySeparatorChar;
			if (!fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
				throw new InvalidOperationException("Diagnostic path escaped its run directory.");
		}
	}

	/// <summary>
	/// Writes files into one store-created failure directory. Saving never transfers ownership of
	/// the supplied bitmap.
	/// </summary>
	internal sealed class FailureDiagnosticScope
	{
		internal FailureDiagnosticScope(string directoryPath)
		{
			DirectoryPath = directoryPath ?? throw new ArgumentNullException(nameof(directoryPath));
		}

		internal string DirectoryPath { get; }

		internal string SaveBitmap(string fileName, Bitmap bitmap)
		{
			if (bitmap == null) throw new ArgumentNullException(nameof(bitmap));
			string path = GetFilePath(fileName);
			bitmap.Save(path);
			return path;
		}

		internal string SaveText(string fileName, string contents)
		{
			string path = GetFilePath(fileName);
			File.WriteAllText(path, contents ?? string.Empty);
			return path;
		}

		private string GetFilePath(string fileName)
		{
			if (string.IsNullOrWhiteSpace(fileName) ||
				!string.Equals(Path.GetFileName(fileName), fileName, StringComparison.Ordinal))
			{
				throw new ArgumentException("Diagnostic file names must be single path segments.", nameof(fileName));
			}

			return Path.Combine(DirectoryPath, fileName);
		}
	}
}

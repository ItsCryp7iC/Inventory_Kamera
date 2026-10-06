using System;
using System.Threading.Tasks;

namespace InventoryKamera
{
    /// <summary>
    /// Runs one queued OCR item while retaining ownership until processing (successful or not) ends.
    /// </summary>
    internal static class OCRImageCollectionProcessor
    {
        internal static async Task ProcessAsync(
            OCRImageCollection work,
            Func<OCRImageCollection, Task> process)
        {
            if (work == null) throw new ArgumentNullException(nameof(work));
            if (process == null) throw new ArgumentNullException(nameof(process));

            using (work)
                await process(work).ConfigureAwait(false);
        }
    }
}

namespace OsCopyPaster.Clipboard;

/// <summary>
/// Low-level clipboard read/write with full data format preservation.
/// All methods are best-effort with try/catch — clipboard operations can fail
/// if another application has the clipboard locked.
/// </summary>
internal static class ClipboardManager
{
    /// <summary>
    /// Deep-copies ALL formats from the current clipboard into a backup dictionary.
    /// Per-format try/catch ensures one failing format doesn't abort the entire backup.
    /// Returns null if the clipboard is empty or inaccessible.
    /// </summary>
    public static Dictionary<string, object?>? Save()
    {
        try
        {
            var dataObject = System.Windows.Forms.Clipboard.GetDataObject();
            if (dataObject == null)
                return null;

            var backup = new Dictionary<string, object?>();
            foreach (var format in dataObject.GetFormats())
            {
                try
                {
                    backup[format] = dataObject.GetData(format);
                }
                catch
                {
                    // Some formats can't be read (COM objects, delayed rendering).
                    // Skip them — best-effort backup.
                }
            }

            return backup.Count > 0 ? backup : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Restores all formats from a backup dictionary onto the clipboard.
    /// Uses retry logic (5 attempts, 50ms spacing) to handle clipboard lock contention.
    /// If backup is null, clears the clipboard instead.
    /// </summary>
    public static void Restore(Dictionary<string, object?>? backup)
    {
        if (backup == null || backup.Count == 0)
        {
            Clear();
            return;
        }

        try
        {
            var dataObject = new DataObject();
            foreach (var (format, data) in backup)
            {
                if (data != null)
                {
                    dataObject.SetData(format, data);
                }
            }

            System.Windows.Forms.Clipboard.SetDataObject(
                dataObject,
                copy: true,
                retryTimes: 5,
                retryDelay: 50);
        }
        catch
        {
            // Best-effort restoration. If it fails, the secondary operation
            // still completed; the user's primary clipboard may need a manual re-copy.
        }
    }

    /// <summary>
    /// Reads text from the clipboard. Returns null if no text is available.
    /// </summary>
    public static string? GetText()
    {
        try
        {
            return System.Windows.Forms.Clipboard.ContainsText()
                ? System.Windows.Forms.Clipboard.GetText()
                : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Writes text to the clipboard with retry logic.
    /// </summary>
    public static void SetText(string text)
    {
        try
        {
            var dataObject = new DataObject();
            dataObject.SetText(text);
            System.Windows.Forms.Clipboard.SetDataObject(
                dataObject,
                copy: true,
                retryTimes: 5,
                retryDelay: 50);
        }
        catch
        {
            // Silently fail — clipboard may be locked by another app.
        }
    }

    /// <summary>
    /// Clears the clipboard contents.
    /// </summary>
    public static void Clear()
    {
        try
        {
            System.Windows.Forms.Clipboard.Clear();
        }
        catch
        {
            // Clipboard may be locked by another app.
        }
    }
}

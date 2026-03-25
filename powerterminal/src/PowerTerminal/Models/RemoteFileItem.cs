namespace PowerTerminal.Models
{
    public class RemoteFileItem
    {
        public string Name        { get; set; } = string.Empty;
        public string FullPath    { get; set; } = string.Empty;
        public bool   IsDirectory { get; set; }
        public bool   IsSymlink   { get; set; }
        public long   Size        { get; set; }
        public string Permissions { get; set; } = string.Empty;
        public string Modified    { get; set; } = string.Empty;
        public string Owner       { get; set; } = string.Empty;

        /// <summary>True if the current SSH user has read permission on this item.</summary>
        public bool CanRead  { get; set; }

        /// <summary>True if the current SSH user has write permission on this item.</summary>
        public bool CanWrite { get; set; }

        /// <summary>Short permission indicator for display: "rw", "r", or "–"</summary>
        public string PermissionDisplay =>
            CanRead && CanWrite ? "rw" :
            CanRead             ? "r"  : "–";

        public string SizeDisplay => IsDirectory ? "" : FormatSize(Size);
        private static string FormatSize(long b) =>
            b < 1024 ? $"{b} B" :
            b < 1024*1024 ? $"{b/1024.0:F1} KB" :
            b < 1024*1024*1024 ? $"{b/(1024.0*1024):F1} MB" :
            $"{b/(1024.0*1024*1024):F1} GB";
    }
}

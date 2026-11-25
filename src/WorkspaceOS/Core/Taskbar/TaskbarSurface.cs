using System;

namespace WorkspaceOS.Core.Taskbar
{
    /// <summary>What the taskbar surface should be.</summary>
    public enum TaskbarBackdropMode
    {
        Disabled,
        Clear,
        Blur,
        Acrylic,
        Dynamic
    }

    /// <summary>
    /// Mode parsing and selection, kept free of Win32/WPF so the rule can be
    /// tested anywhere: a desktop has nothing to sit behind (clear), a maximized
    /// window owns the screen (stronger acrylic), everything else is light acrylic.
    /// </summary>
    public static class TaskbarSurface
    {
        public static TaskbarBackdropMode Parse(string raw) =>
            (raw ?? "").Trim().ToLowerInvariant() switch
            {
                "disabled" or "off" or "none" => TaskbarBackdropMode.Disabled,
                "clear" or "transparent" => TaskbarBackdropMode.Clear,
                "blur" => TaskbarBackdropMode.Blur,
                "acrylic" => TaskbarBackdropMode.Acrylic,
                _ => TaskbarBackdropMode.Dynamic
            };

        public static TaskbarBackdropMode Resolve(TaskbarBackdropMode configured,
            bool desktopForeground, bool maximized) => configured switch
        {
            TaskbarBackdropMode.Dynamic => desktopForeground
                ? TaskbarBackdropMode.Clear
                : maximized ? TaskbarBackdropMode.Acrylic : TaskbarBackdropMode.Blur,
            _ => configured
        };
    }
}

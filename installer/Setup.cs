// WorkspaceOS Setup — self-extracting installer.
// Compiled with the in-box .NET Framework C# compiler (C# 5 syntax).
// The application exe is embedded as a resource named "WorkspaceOS.exe".
//
// Usage:
//   WorkspaceOS-Setup.exe            interactive install
//   WorkspaceOS-Setup.exe /S         silent install
//   WorkspaceOS-Setup.exe /uninstall uninstall

using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.Win32;

namespace WorkspaceOSSetup
{
    internal static class Program
    {
        private const string AppName = "WorkspaceOS";
        private const string ExeName = "WorkspaceOS.exe";
        private const string AhkResource = "AutoHotkey64.exe";
        private const string AhkSubdir = "AutoHotkey";
        private const string Version = "2.0.1";

        private static string InstallDir
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Programs", AppName);
            }
        }

        [STAThread]
        private static int Main(string[] args)
        {
            bool silent = false, uninstall = false;
            foreach (string a in args)
            {
                if (string.Equals(a, "/S", StringComparison.OrdinalIgnoreCase)) silent = true;
                if (string.Equals(a, "/uninstall", StringComparison.OrdinalIgnoreCase)) uninstall = true;
            }

            try
            {
                if (uninstall) return Uninstall(silent);
                return Install(silent);
            }
            catch (Exception ex)
            {
                if (!silent)
                    MessageBox.Show("Setup failed:\r\n" + ex.Message, AppName + " Setup",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }

        private static int Install(bool silent)
        {
            if (!silent)
            {
                DialogResult r = MessageBox.Show(
                    "Install WorkspaceOS 2.0?\r\n\r\n" +
                    "• Hyprland-inspired tiling window manager (BSP/Dwindle)\r\n" +
                    "• Workspaces with real Win+1..9 hotkeys (AutoHotkey-powered)\r\n" +
                    "• Directional focus/move/resize, floating, scratchpad\r\n" +
                    "• Polybar-style top bar, focus mode, launcher, screenshots\r\n\r\n" +
                    "Install location:\r\n" + InstallDir + "\r\n\r\n" +
                    "The AutoHotkey runtime is bundled — nothing else to install.\r\n" +
                    "WorkspaceOS will start automatically with Windows.",
                    AppName + " Setup", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
                if (r != DialogResult.OK) return 2;
            }

            // Stop a running instance so the exe can be replaced.
            foreach (Process p in Process.GetProcessesByName("WorkspaceOS"))
            {
                try { p.Kill(); p.WaitForExit(5000); } catch { }
            }

            Directory.CreateDirectory(InstallDir);
            Directory.CreateDirectory(Path.Combine(InstallDir, AhkSubdir));
            string target = Path.Combine(InstallDir, ExeName);

            // Extract the embedded application.
            Assembly asm = Assembly.GetExecutingAssembly();
            using (Stream src = asm.GetManifestResourceStream(ExeName))
            {
                if (src == null) throw new Exception("Embedded payload missing.");
                using (FileStream dst = File.Create(target))
                {
                    src.CopyTo(dst);
                }
            }

            // Extract the bundled AutoHotkey v2 runtime (used for Win+1..9 hotkeys).
            string ahkTarget = Path.Combine(InstallDir, AhkSubdir, "AutoHotkey64.exe");
            using (Stream src = asm.GetManifestResourceStream(AhkResource))
            {
                if (src != null)
                {
                    using (FileStream dst = File.Create(ahkTarget))
                    {
                        src.CopyTo(dst);
                    }
                }
                // A missing AHK payload is not fatal: WorkspaceOS falls back to its C# hook.
            }

            // Copy setup next to the app to serve as the uninstaller.
            string uninstaller = Path.Combine(InstallDir, "Uninstall.exe");
            try { File.Copy(Assembly.GetExecutingAssembly().Location, uninstaller, true); } catch { }

            // Start Menu shortcut.
            CreateShortcut(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppName + ".lnk"),
                target);

            // Run at startup.
            using (RegistryKey run = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run", true))
            {
                if (run != null) run.SetValue(AppName, "\"" + target + "\"");
            }

            // Add/Remove Programs entry.
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + AppName))
            {
                if (key != null)
                {
                    key.SetValue("DisplayName", AppName);
                    key.SetValue("DisplayVersion", Version);
                    key.SetValue("Publisher", AppName);
                    key.SetValue("InstallLocation", InstallDir);
                    key.SetValue("DisplayIcon", target);
                    key.SetValue("UninstallString", "\"" + uninstaller + "\" /uninstall");
                    key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                    key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                }
            }

            // Launch now.
            Process.Start(target);

            if (!silent)
                MessageBox.Show(
                    "WorkspaceOS 2.0 installed and running.\r\n\r\n" +
                    "Win+1..4        switch workspaces (no taskbar apps!)\r\n" +
                    "Win+H/J/K/L     focus left/down/up/right\r\n" +
                    "Win+Shift+H..L  move windows directionally\r\n" +
                    "Win+Ctrl+H..L   resize splits\r\n" +
                    "Win+Shift+Space float / tile window\r\n" +
                    "Alt+Space       launcher\r\n" +
                    "Win+Shift+T     tiling on/off\r\n\r\n" +
                    "Tiling is configured in Settings → Tiling.",
                    AppName + " Setup", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 0;
        }

        private static int Uninstall(bool silent)
        {
            if (!silent)
            {
                DialogResult r = MessageBox.Show("Remove WorkspaceOS?", AppName + " Setup",
                    MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
                if (r != DialogResult.OK) return 2;
            }

            foreach (Process p in Process.GetProcessesByName("WorkspaceOS"))
            {
                try { p.Kill(); p.WaitForExit(5000); } catch { }
            }

            using (RegistryKey run = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run", true))
            {
                if (run != null) run.DeleteValue(AppName, false);
            }
            Registry.CurrentUser.DeleteSubKeyTree(
                @"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + AppName, false);

            try
            {
                File.Delete(Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppName + ".lnk"));
            }
            catch { }

            try { File.Delete(Path.Combine(InstallDir, ExeName)); } catch { }
            try { File.Delete(Path.Combine(InstallDir, AhkSubdir, "AutoHotkey64.exe")); } catch { }
            try { Directory.Delete(Path.Combine(InstallDir, AhkSubdir), false); } catch { }

            // Delete install dir (self-deleting uninstaller via cmd).
            string cmd = "/c ping 127.0.0.1 -n 3 > nul & rmdir /s /q \"" + InstallDir + "\"";
            ProcessStartInfo psi = new ProcessStartInfo("cmd.exe", cmd);
            psi.WindowStyle = ProcessWindowStyle.Hidden;
            psi.CreateNoWindow = true;
            Process.Start(psi);

            if (!silent)
                MessageBox.Show("WorkspaceOS removed.\r\nYour settings in %APPDATA%\\WorkspaceOS were kept.",
                    AppName + " Setup", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 0;
        }

        private static void CreateShortcut(string lnkPath, string targetPath)
        {
            Type shellType = Type.GetTypeFromProgID("WScript.Shell");
            object shell = Activator.CreateInstance(shellType);
            object shortcut = shellType.InvokeMember("CreateShortcut",
                BindingFlags.InvokeMethod, null, shell, new object[] { lnkPath });
            Type scType = shortcut.GetType();
            scType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, new object[] { targetPath });
            scType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, shortcut,
                new object[] { Path.GetDirectoryName(targetPath) });
            scType.InvokeMember("Description", BindingFlags.SetProperty, null, shortcut,
                new object[] { "WorkspaceOS — productivity desktop environment" });
            scType.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
        }
    }
}

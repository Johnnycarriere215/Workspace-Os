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
using System.IO.Pipes;
using System.Reflection;
using System.Text;
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
        private const string Version = "5.1.0";

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
                // Silent installs have no dialog, so always leave a trace for diagnosis.
                try
                {
                    File.AppendAllText(
                        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                            AppName, "setup.log"),
                        DateTime.Now.ToString("s") + "  " + ex + Environment.NewLine);
                }
                catch { }
                if (!silent)
                    MessageBox.Show("Setup failed:\r\n" + ex.Message, AppName + " Setup",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }

        private static int Install(bool silent)
        {
            if (!Environment.Is64BitOperatingSystem)
            {
                if (!silent)
                    MessageBox.Show("WorkspaceOS requires 64-bit Windows 10 or 11.",
                        AppName + " Setup", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }

            if (!silent)
            {
                DialogResult r = MessageBox.Show(
                    "Install WorkspaceOS 5.1.0?\r\n\r\n" +
                    "• Fourteen Omarchy themes, cycled with Ctrl+Win+Shift+Space\r\n" +
                    "• Drop-in theme folders: any Omarchy colors.toml works\r\n" +
                    "• Bar forms (Full, Fit, Dock, Notch) and logo menu on the bar\r\n" +
                    "• cava audio bars in the centre cluster, painted by the theme\r\n" +
                    "• Shibumi shell: single-run bar, pill widgets and detail panels\r\n" +
                    "• Themed Windows taskbar preset for Windhawk\r\n" +
                    "• Theme wallpapers on every monitor and virtual desktop\r\n" +
                    "• Set any theme or Unsplash photo as your desktop background\r\n" +
                    "• New app windows open maximized once, then stay movable and resizable\r\n" +
                    "• Fullscreen games and browser video hide the bar and release its reserved screen space\r\n" +
                    "• Focus follows the mouse after a short hover, immediately after a workspace switch\r\n" +
                    "• Workspaces with real Win+1..9 hotkeys (AutoHotkey-powered)\r\n" +
                    "• Windows 10 + every Windows 11 build supported (24H2 included)\r\n" +
                    "• Themeable top bar, focus mode, launcher and clipboard manager\r\n" +
                    "• Move a window across monitors with Win+Shift+Arrow\r\n" +
                    "• Win+Space opens the Start menu; Alt+Space opens the launcher\r\n" +
                    "• Win+S toggles the scratchpad; Ctrl+Win+S stashes the focused window\r\n\r\n" +
                    "Install location:\r\n" + InstallDir + "\r\n\r\n" +
                    "The AutoHotkey runtime is bundled — nothing else to install.\r\n" +
                    "WorkspaceOS will start automatically with Windows.",
                    AppName + " Setup", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
                if (r != DialogResult.OK) return 2;
            }

            // Stop a running instance (and its bundled AHK, which locks the runtime
            // file) so the payload can be replaced.
            StopRunningInstances();

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

            // Launch now. The previous instance may still be releasing the
            // single-instance mutex, in which case the new one exits immediately.
            Process.Start(target);
            if (!WaitForApp(2500))
            {
                System.Threading.Thread.Sleep(1500);
                Process.Start(target);
                WaitForApp(2500);
            }

            if (!silent)
                MessageBox.Show(
                    "WorkspaceOS 5.1.0 installed and running.\r\n\r\n" +
                    "Win+1..4        switch workspaces (no taskbar apps!)\r\n" +
                    "Win+Shift+Arrow move focused window to another monitor\r\n" +
                    "Win+W / Win+M / Win+F  close / maximize / fullscreen\r\n" +
                    "Win+Space       Start menu (the Windows-key menu)\r\n" +
                    "Alt+Space       WorkspaceOS launcher\r\n" +
                    "\r\n" +
                    "New app windows open maximized; restore or drag them normally. Fullscreen games are left alone.",
                    AppName + " Setup", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 0;
        }

        private static void ExtractResource(Assembly asm, string name, string target)
        {
            using (Stream src = asm.GetManifestResourceStream(name))
            {
                if (src == null) return;
                using (FileStream dst = File.Create(target)) src.CopyTo(dst);
            }
        }

        private static int Uninstall(bool silent)
        {
            if (!silent)
            {
                DialogResult r = MessageBox.Show("Remove WorkspaceOS?", AppName + " Setup",
                    MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
                if (r != DialogResult.OK) return 2;
            }

            StopRunningInstances();

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

        /// <summary>
        /// Stops the app and its bundled AutoHotkey runtime. Only AHK instances
        /// running from this install directory are touched, so the user's own
        /// AutoHotkey scripts are never killed.
        /// </summary>
        private static void StopRunningInstances()
        {
            // Ask first: a clean exit restores a stashed scratchpad window and
            // removes its pin. Killing leaves that window hidden (still playing
            // audio) until the next launch recovers it from its state file.
            RequestGracefulExit();

            foreach (Process p in Process.GetProcessesByName("WorkspaceOS"))
            {
                try { p.Kill(); p.WaitForExit(5000); } catch { }
            }

            foreach (Process p in Process.GetProcessesByName("AutoHotkey64"))
            {
                try
                {
                    string path = p.MainModule.FileName;
                    if (path != null && path.StartsWith(InstallDir, StringComparison.OrdinalIgnoreCase))
                    {
                        p.Kill();
                        p.WaitForExit(5000);
                    }
                }
                catch { }
            }
        }

        private static bool WaitForApp(int timeoutMs)
        {
            int waited = 0;
            while (waited < timeoutMs)
            {
                if (Process.GetProcessesByName("WorkspaceOS").Length > 0) return true;
                System.Threading.Thread.Sleep(250);
                waited += 250;
            }
            return Process.GetProcessesByName("WorkspaceOS").Length > 0;
        }

        /// <summary>Sends "exit" to the running app's command pipe (best effort).</summary>
        private static void RequestGracefulExit()
        {
            try
            {
                using (NamedPipeClientStream pipe = new NamedPipeClientStream(
                    ".", "WorkspaceOS.Ctrl", PipeDirection.InOut))
                {
                    pipe.Connect(800);
                    byte[] payload = Encoding.UTF8.GetBytes("exit\n");
                    pipe.Write(payload, 0, payload.Length);
                    pipe.Flush();
                    System.Threading.Thread.Sleep(1500);
                }
            }
            catch { }
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

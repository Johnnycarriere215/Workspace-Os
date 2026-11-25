using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

internal static class Shot
{
    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int L, T, R, B; }

    private delegate bool EnumProc(IntPtr h, IntPtr l);

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc p, IntPtr l);
    [DllImport("user32.dll")] private static extern int GetWindowThreadProcessId(IntPtr h, out int pid);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] private static extern int GetWindowText(IntPtr h, StringBuilder s, int m);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr h, int attr, out int value, int size);
    [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] private static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern void mouse_event(uint f, uint dx, uint dy, uint d, UIntPtr e);
    [DllImport("user32.dll")] private static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);

    private static void Main(string[] args)
    {
        SetProcessDPIAware();
        if (args.Length == 0) return;

        switch (args[0])
        {
            case "capture":
                Capture(args.Length > 1 ? args[1] : "shot.jpg",
                        args.Length > 2 ? int.Parse(args[2]) : 1600);
                break;
            case "click":
                Click(int.Parse(args[1]), int.Parse(args[2]));
                Console.WriteLine("clicked " + args[1] + "," + args[2]);
                break;
            case "windows":
                foreach (var line in Windows(args.Length > 1 ? args[1] : ""))
                    Console.WriteLine(line);
                break;
            case "key":
                keybd_event((byte)int.Parse(args[1]), 0, 0, UIntPtr.Zero);
                System.Threading.Thread.Sleep(60);
                keybd_event((byte)int.Parse(args[1]), 0, 0x0002, UIntPtr.Zero);
                Console.WriteLine("key " + args[1]);
                break;
            case "press":
                SetCursorPos(int.Parse(args[1]), int.Parse(args[2]));
                System.Threading.Thread.Sleep(150);
                mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero);
                Console.WriteLine("pressed");
                break;
            case "moveto":
                SetCursorPos(int.Parse(args[1]), int.Parse(args[2]));
                Console.WriteLine("moved");
                break;
            case "release":
                mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero);
                Console.WriteLine("released");
                break;
            case "printwin":
                CaptureWindow(args[1], args[2]);
                break;
            case "printwinhandle":
                CaptureWindowHandle(new IntPtr(long.Parse(args[1])), args[2]);
                break;
            case "foreground":
                ShowWindow(new IntPtr(long.Parse(args[1])), 9); // SW_RESTORE
                SetForegroundWindow(new IntPtr(long.Parse(args[1])));
                Console.WriteLine("focused " + args[1]);
                break;
            case "drag":
                Drag(int.Parse(args[1]), int.Parse(args[2]), int.Parse(args[3]), int.Parse(args[4]));
                Console.WriteLine("dragged");
                break;
            case "film":
                Film(args[1], int.Parse(args[2]), int.Parse(args[3]),
                     int.Parse(args[4]), int.Parse(args[5]), int.Parse(args[6]), int.Parse(args[7]));
                break;
            case "trail":
                Trail(args[1], int.Parse(args[2]));
                break;
            case "crop":
                Crop(args[1], int.Parse(args[2]), int.Parse(args[3]), int.Parse(args[4]), int.Parse(args[5]), args[6]);
                break;
            case "tone":
                Tone(args.Length > 1 ? int.Parse(args[1]) : 6);
                break;
            case "chord":
                Chord(args[1]);
                Console.WriteLine("sent " + args[1]);
                break;
            case "screen":
                var s = SystemInformation.VirtualScreen;
                Console.WriteLine(s.Width + "x" + s.Height + " at " + s.Left + "," + s.Top);
                break;
        }
    }

    /// <summary>Captures a fixed region repeatedly, for checking motion frame by frame.</summary>
    private static void Film(string dir, int frames, int interval, int x, int y, int w, int h)
    {
        System.IO.Directory.CreateDirectory(dir);
        var screen = SystemInformation.VirtualScreen;
        for (int i = 0; i < frames; i++)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            using (var bmp = new Bitmap(w, h))
            {
                using (var g = Graphics.FromImage(bmp))
                    g.CopyFromScreen(screen.Left + x, screen.Top + y, 0, 0, new Size(w, h));
                bmp.Save(System.IO.Path.Combine(dir, "f" + i.ToString("00") + ".png"), ImageFormat.Png);
            }
            int left = interval - (int)clock.ElapsedMilliseconds;
            if (left > 0) System.Threading.Thread.Sleep(left);
        }
        Console.WriteLine("film " + frames + " frames in " + dir);
    }

    /// <summary>Samples one window's rectangle, printing every distinct value.</summary>
    private static void Trail(string prefix, int ms)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        IntPtr target = IntPtr.Zero;
        RECT last = new RECT();
        bool first = true;
        while (clock.ElapsedMilliseconds < ms)
        {
            if (target == IntPtr.Zero || !IsWindow(target))
            {
                target = IntPtr.Zero;
                EnumWindows((h, l) =>
                {
                    var text = new StringBuilder(256);
                    GetWindowText(h, text, 256);
                    if (text.ToString().StartsWith(prefix))
                    {
                        int corner = -1;
                        DwmGetWindowAttribute(h, 14, out corner, 4);  // DWMWA_CLOAKED
                        if (corner == 0) { target = h; return false; }
                    }
                    return true;
                }, IntPtr.Zero);
            }
            RECT r;
            if (target != IntPtr.Zero && GetWindowRect(target, out r))
            {
                if (first || r.L != last.L || r.T != last.T || r.R != last.R || r.B != last.B)
                {
                    Console.WriteLine(clock.ElapsedMilliseconds + "ms " + r.L + "," + r.T + " " +
                                      (r.R - r.L) + "x" + (r.B - r.T));
                    last = r;
                    first = false;
                }
            }
            System.Threading.Thread.Sleep(6);
        }
    }

    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr h);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr h, int command);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr h);

    private static void Crop(string source, int x, int y, int w, int h, string target)
    {
        using (var image = Image.FromFile(source))
        using (var crop = new Bitmap(w, h))
        {
            using (var g = Graphics.FromImage(crop))
                g.DrawImage(image, new Rectangle(0, 0, w, h), new Rectangle(x, y, w, h), GraphicsUnit.Pixel);
            crop.Save(target, ImageFormat.Png);
        }
        Console.WriteLine("cropped " + target);
    }

    /// <summary>Plays a short generated sweep so cava has real audio to read.</summary>
    private static void Tone(int seconds)
    {
        const int rate = 44100;
        int samples = rate * seconds;
        using (var stream = new System.IO.MemoryStream())
        using (var writer = new System.IO.BinaryWriter(stream))
        {
            writer.Write(new[] { 'R', 'I', 'F', 'F' });
            writer.Write(36 + samples * 2);
            writer.Write(new[] { 'W', 'A', 'V', 'E', 'f', 'm', 't', ' ' });
            writer.Write(16); writer.Write((short)1); writer.Write((short)1);
            writer.Write(rate); writer.Write(rate * 2); writer.Write((short)2); writer.Write((short)16);
            writer.Write(new[] { 'd', 'a', 't', 'a' });
            writer.Write(samples * 2);
            for (int i = 0; i < samples; i++)
            {
                double t = i / (double)rate;
                // Sweep and pulse so the bars move instead of sitting still.
                double freq = 180 + 520 * (0.5 + 0.5 * Math.Sin(2 * Math.PI * 0.35 * t));
                double env = 0.5 + 0.5 * Math.Sin(2 * Math.PI * 2.2 * t);
                writer.Write((short)(Math.Sin(2 * Math.PI * freq * t) * env * 9000));
            }
            stream.Position = 0;
            using (var player = new System.Media.SoundPlayer(stream))
            {
                player.PlaySync();
            }
        }
        Console.WriteLine("played " + seconds + "s tone");
    }

    private static void Chord(string spec)
    {
        var mods = new System.Collections.Generic.List<byte>();
        foreach (var part in spec.Split('+'))
        {
            switch (part.ToLowerInvariant())
            {
                case "alt": mods.Add(0x12); break;
                case "shift": mods.Add(0x10); break;
                case "ctrl": mods.Add(0x11); break;
                case "win": mods.Add(0x5B); break;
            }
        }
        string keyPart = spec.Substring(spec.LastIndexOf('+') + 1);
        byte vk = keyPart.Length == 1 ? (byte)char.ToUpperInvariant(keyPart[0]) : (byte)0x70;

        const uint KEYUP = 0x0002;
        foreach (var mod in mods) keybd_event(mod, 0, 0, UIntPtr.Zero);
        System.Threading.Thread.Sleep(60);
        keybd_event(vk, 0, 0, UIntPtr.Zero);
        System.Threading.Thread.Sleep(60);
        keybd_event(vk, 0, KEYUP, UIntPtr.Zero);
        System.Threading.Thread.Sleep(60);
        for (int i = mods.Count - 1; i >= 0; i--) keybd_event(mods[i], 0, KEYUP, UIntPtr.Zero);
    }

    /// <summary>Presses, moves in steps (so the WM sees a drag) and releases.</summary>
    private static void Drag(int x1, int y1, int x2, int y2)
    {
        SetCursorPos(x1, y1);
        System.Threading.Thread.Sleep(200);
        mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero);
        System.Threading.Thread.Sleep(120);
        const int steps = 14;
        for (int i = 1; i <= steps; i++)
        {
            SetCursorPos(x1 + (x2 - x1) * i / steps, y1 + (y2 - y1) * i / steps);
            System.Threading.Thread.Sleep(35);
        }
        System.Threading.Thread.Sleep(150);
        mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero);
        System.Threading.Thread.Sleep(300);
    }

    private static void Click(int x, int y)
    {
        SetCursorPos(x, y);
        System.Threading.Thread.Sleep(150);
        mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero);
        System.Threading.Thread.Sleep(80);
        mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero);
    }

    // Renders one window through WM_PRINT. Works when CopyFromScreen cannot
    // (locked or disconnected desktop), which is what happened to the bar shots.
    private static void CaptureWindow(string title, string path)
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows((h, l) =>
        {
            var sb = new StringBuilder(256);
            GetWindowText(h, sb, 256);
            if (sb.ToString().IndexOf(title, StringComparison.OrdinalIgnoreCase) >= 0) { found = h; return false; }
            return true;
        }, IntPtr.Zero);

        if (found == IntPtr.Zero) { Console.WriteLine("printwin: window not found"); return; }
        CaptureWindowHandle(found, path);
    }

    private static void CaptureWindowHandle(IntPtr found, string path)
    {

        RECT r;
        GetWindowRect(found, out r);
        int w = Math.Max(1, r.R - r.L), hgt = Math.Max(1, r.B - r.T);
        using (var bmp = new Bitmap(w, hgt))
        {
            using (var g = Graphics.FromImage(bmp))
            {
                IntPtr hdc = g.GetHdc();
                bool ok = PrintWindow(found, hdc, 2); // PW_RENDERFULLCONTENT
                g.ReleaseHdc(hdc);
                Console.WriteLine("printwin " + ok + " " + w + "x" + hgt);
            }
            bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        }
    }

    private static void Capture(string path, int width)
    {
        var screen = SystemInformation.VirtualScreen;
        using (var full = new Bitmap(screen.Width, screen.Height))
        {
            using (var g = Graphics.FromImage(full))
                g.CopyFromScreen(screen.Left, screen.Top, 0, 0, full.Size);

            double scale = Math.Min(1.0, width / (double)screen.Width);
            int w = (int)(screen.Width * scale);
            int h = (int)(screen.Height * scale);

            using (var small = new Bitmap(w, h))
            {
                using (var sg = Graphics.FromImage(small))
                {
                    sg.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    sg.DrawImage(full, 0, 0, w, h);
                }
                var codec = Array.Find(ImageCodecInfo.GetImageEncoders(), c => c.MimeType == "image/jpeg");
                var pars = new EncoderParameters(1);
                pars.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 84L);
                small.Save(path, codec, pars);
            }
        }
        Console.WriteLine("saved " + path);
    }

    private static List<string> Windows(string prefix)
    {
        var list = new List<string>();
        EnumWindows((h, l) =>
        {
            var text = new StringBuilder(256);
            GetWindowText(h, text, 256);
            string title = text.ToString();
            if (prefix.Length > 0 && !title.StartsWith(prefix)) return true;

            RECT r;
            GetWindowRect(h, out r);
            int corner = -1;
            DwmGetWindowAttribute(h, 33, out corner, 4);   // DWMWA_WINDOW_CORNER_PREFERENCE
            int cloaked = 0;
            DwmGetWindowAttribute(h, 14, out cloaked, 4);  // DWMWA_CLOAKED
            int pid;
            GetWindowThreadProcessId(h, out pid);
            list.Add(string.Format("'{0}' pid {1} handle {8} rect {2},{3} {4}x{5} cornerPref {6} cloaked {7}",
                title, pid, r.L, r.T, r.R - r.L, r.B - r.T, corner, cloaked, h.ToInt64()));
            return true;
        }, IntPtr.Zero);
        return list;
    }
}

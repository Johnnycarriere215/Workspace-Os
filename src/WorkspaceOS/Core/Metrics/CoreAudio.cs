using System;
using System.Runtime.InteropServices;

namespace WorkspaceOS.Core.Metrics
{
    /// <summary>Minimal CoreAudio interop for reading/setting master volume.</summary>
    public static class CoreAudio
    {
        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
        private class MMDeviceEnumeratorComObject { }

        [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceEnumerator
        {
            int NotImpl1();
            int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice ppDevice);
        }

        [Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDevice
        {
            int Activate(ref Guid iid, int dwClsCtx, IntPtr pActivationParams, [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
        }

        [Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAudioEndpointVolume
        {
            int RegisterControlChangeNotify(IntPtr pNotify);
            int UnregisterControlChangeNotify(IntPtr pNotify);
            int GetChannelCount(out uint pnChannelCount);
            int SetMasterVolumeLevel(float fLevelDB, ref Guid pguidEventContext);
            int SetMasterVolumeLevelScalar(float fLevel, ref Guid pguidEventContext);
            int GetMasterVolumeLevel(out float pfLevelDB);
            int GetMasterVolumeLevelScalar(out float pfLevel);
            int SetChannelVolumeLevel(uint nChannel, float fLevelDB, ref Guid pguidEventContext);
            int SetChannelVolumeLevelScalar(uint nChannel, float fLevel, ref Guid pguidEventContext);
            int GetChannelVolumeLevel(uint nChannel, out float pfLevelDB);
            int GetChannelVolumeLevelScalar(uint nChannel, out float pfLevel);
            int SetMute(bool bMute, ref Guid pguidEventContext);
            int GetMute(out bool pbMute);
        }

        private static IAudioEndpointVolume GetEndpoint()
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            enumerator.GetDefaultAudioEndpoint(0 /*eRender*/, 1 /*eMultimedia*/, out var device);
            var iid = typeof(IAudioEndpointVolume).GUID;
            device.Activate(ref iid, 1 /*CLSCTX_INPROC_SERVER*/, IntPtr.Zero, out var o);
            return (IAudioEndpointVolume)o;
        }

        public static float GetMasterVolume()
        {
            var ep = GetEndpoint();
            try { ep.GetMasterVolumeLevelScalar(out float level); return level; }
            finally { Marshal.ReleaseComObject(ep); }
        }

        public static bool GetMasterMute()
        {
            var ep = GetEndpoint();
            try { ep.GetMute(out bool mute); return mute; }
            finally { Marshal.ReleaseComObject(ep); }
        }

        public static void SetMasterVolume(float level)
        {
            var ep = GetEndpoint();
            try { var g = Guid.Empty; ep.SetMasterVolumeLevelScalar(Math.Clamp(level, 0f, 1f), ref g); }
            finally { Marshal.ReleaseComObject(ep); }
        }
    }
}

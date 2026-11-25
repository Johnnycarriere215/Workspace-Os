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
            int OpenPropertyStore(int access, out IntPtr store);
            int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
            int GetState(out int state);
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
            int GetVolumeStepInfo(out uint pnStep, out uint pnStepCount);
            int VolumeStepUp(ref Guid pguidEventContext);
            int VolumeStepDown(ref Guid pguidEventContext);
            int QueryHardwareSupport(out uint pdwHardwareSupportMask);
            int GetVolumeRange(out float pflVolumeMindB, out float pflVolumeMaxdB,
                out float pflVolumeIncrementdB);
        }

        private static IMMDevice GetDevice()
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            enumerator.GetDefaultAudioEndpoint(0 /*eRender*/, 1 /*eMultimedia*/, out var device);
            return device;
        }

        private static IAudioEndpointVolume GetEndpoint()
        {
            var device = GetDevice();
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

        public static void SetMasterMute(bool mute)
        {
            var ep = GetEndpoint();
            try { var g = Guid.Empty; ep.SetMute(mute, ref g); }
            finally { Marshal.ReleaseComObject(ep); }
        }

        // ---- volume boost ---------------------------------------------------
        //
        // Windows' 0–100 % slider is the endpoint's 0 dB point. A few outputs
        // (USB DACs, some laptop amps) also expose headroom above 0 dB; Windows
        // rejects anything past the endpoint's own maximum with E_INVALIDARG, so
        // the ceiling has to be read from the device, never assumed.

        /// <summary>The default output's endpoint id, as the device registry keys it.</summary>
        public static string DefaultDeviceId()
        {
            try
            {
                var device = GetDevice();
                return device.GetId(out string id) == 0 ? id : null;
            }
            catch { return null; }
        }

        /// <summary>The endpoint's level range in dB, as the driver reports it.</summary>
        public static (float MinDb, float MaxDb) GetVolumeRange()
        {
            var ep = GetEndpoint();
            try
            {
                ep.GetVolumeRange(out float min, out float max, out _);
                return (min, max);
            }
            finally { Marshal.ReleaseComObject(ep); }
        }

        /// <summary>Master level in dB (0 = the 100 % mark).</summary>
        public static float GetMasterVolumeDb()
        {
            var ep = GetEndpoint();
            try { ep.GetMasterVolumeLevel(out float db); return db; }
            finally { Marshal.ReleaseComObject(ep); }
        }

        /// <summary>Sets the level in dB; false when the endpoint rejects it.</summary>
        public static bool SetMasterVolumeDb(float db)
        {
            var ep = GetEndpoint();
            try
            {
                var g = Guid.Empty;
                return ep.SetMasterVolumeLevel(db, ref g) == 0;
            }
            finally { Marshal.ReleaseComObject(ep); }
        }

        /// <summary>
        /// The largest percentage this output accepts. 100 when the endpoint has
        /// no headroom above 0 dB (the usual case), otherwise the amplitude ratio
        /// of that headroom: +6 dB = 200 %.
        /// </summary>
        public static double MaxPercentFor(float maxDb) =>
            maxDb <= 0.01f ? 100.0 : 100.0 * Math.Pow(10, maxDb / 20.0);

        /// <summary>
        /// The ceiling the volume slider spans: the gain stage when it is attached
        /// (200 %), otherwise whatever dB headroom the driver exposes (usually 0).
        /// </summary>
        public static double GetMaxPercent()
        {
            double stage = GainStage.MaxPercent;
            double device = 100.0;
            try { device = MaxPercentFor(GetVolumeRange().MaxDb); } catch { }
            return Math.Max(stage, device);
        }

        /// <summary>
        /// Volume in percent, including boost. Up to 100 % this is the shell's own
        /// scalar curve; above it the endpoint reports dB over 0, which converts
        /// straight back to a percentage of amplitude.
        /// </summary>
        public static double GetVolumePercent()
        {
            var ep = GetEndpoint();
            try
            {
                ep.GetMasterVolumeLevelScalar(out float scalar);
                ep.GetMasterVolumeLevel(out float db);
                double endpoint = db > 0.01f
                    ? 100.0 * Math.Pow(10, db / 20.0)
                    : Math.Clamp(scalar, 0f, 1f) * 100.0;
                return endpoint * GainStage.Gain;
            }
            finally { Marshal.ReleaseComObject(ep); }
        }

        /// <summary>
        /// Sets the level from a percentage, mapping 100 %→200 % onto the output's
        /// dB headroom. Values past the device ceiling are clamped there instead of
        /// failing, so the slider never lies about what it applied.
        /// </summary>
        public static void SetVolumePercent(double percent)
        {
            double ceiling = Math.Max(100.0, GetMaxPercent());
            percent = Math.Clamp(percent, 0, ceiling);

            // Above 100 % the endpoint sits at full scale and the preamp carries the
            // rest; at or below it the preamp leaves the path entirely.
            bool boosted = percent > 100.0;
            if (GainStage.Available) GainStage.SetGain(boosted ? percent / 100.0 : 1.0);

            if (!boosted)
            {
                SetMasterVolume((float)(percent / 100.0));
                return;
            }

            if (GainStage.Available)
            {
                SetMasterVolume(1f);
                return;
            }

            // No preamp: the only other place a level can exceed 0 dB is the
            // driver's own dB headroom, and only up to what it reports.
            float maxDb = GetVolumeRange().MaxDb;
            float db = (float)(20.0 * Math.Log10(percent / 100.0));
            SetMasterVolumeDb(Math.Clamp(db, 0f, Math.Max(0f, maxDb)));
        }
    }
}

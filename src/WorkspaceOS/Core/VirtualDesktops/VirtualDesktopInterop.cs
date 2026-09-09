using System;
using System.Runtime.InteropServices;

namespace WorkspaceOS.Core.VirtualDesktops
{
    // Interop for the Windows shell's internal Virtual Desktop COM interfaces.
    //
    // Windows only documents the read-only IVirtualDesktopManager. Switching
    // desktops programmatically and moving OTHER processes' windows requires
    // the immersive shell's internal services — which Microsoft keeps changing:
    //
    //   • Windows 10 (builds < 22000): IVirtualDesktop {FF72FFDD-…} with
    //     2 methods, IVirtualDesktopManagerInternal {F31574D6-…} with 10.
    //   • Windows 11 21H2–23H2 (builds 22000–22631): IVirtualDesktop gains
    //     GetName/GetWallpaperPath/IsRemote (GUID {3F07F4BE-…}); the manager
    //     becomes {53F5CA0B-…} and gains SwitchDesktopAndMoveForegroundView
    //     (later builds) and MoveDesktop.
    //   • Windows 11 24H2/25H2 (builds ≥ 26100): SAME manager GUID
    //     {53F5CA0B-…} but the vtable grew: SwitchDesktopAndMoveForegroundView
    //     sits between SwitchDesktop and CreateDesktop. Same GUID, different
    //     method order — only the OS build number can tell the two apart, and
    //     calling CreateDesktop with the wrong declaration hits
    //     SwitchDesktopAndMoveForegroundView instead (it switches desktops and
    //     drags the foreground window along — visible as chaos).
    //
    // VirtualDesktopService probes the candidates (primary set chosen by OS
    // build) and keeps the first that answers a sanity call, falling back to
    // Ctrl+Win+Arrow simulation if none matches (e.g. a future Windows version).

    internal static class VdGuids
    {
        public static readonly Guid CLSID_ImmersiveShell = new("C2F03A33-21F5-47FA-B4BB-156362A2F239");
        public static readonly Guid CLSID_VirtualDesktopManagerInternal = new("C5E0CDCA-7B6E-41B2-9FC4-D93975CC467B");
        public static readonly Guid CLSID_VirtualDesktopPinnedApps = new("B5A399E7-1C87-46B8-88E9-FC5747B171BD");
        public static readonly Guid IID_IApplicationView = new("372E1D3B-38D3-42E4-A15B-8AB2B178F513");

        public static readonly Guid IID_IVirtualDesktop_Win10 = new("FF72FFDD-BE7E-43FC-9C03-AD81681E88E4");
        public static readonly Guid IID_IVirtualDesktop_Win11 = new("3F07F4BE-B107-441A-AF0F-39D82529072C");
        public static readonly Guid IID_IVirtualDesktopManagerInternal_Win10 = new("F31574D6-B682-4CDC-BD56-1827860ABEC6");
        public static readonly Guid IID_IVirtualDesktopManagerInternal_Win11 = new("53F5CA0B-158F-4124-900C-057158060B27");
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("6D5140C1-7436-11CE-8034-00AA006009FA")]
    internal interface IServiceProvider10
    {
        [return: MarshalAs(UnmanagedType.IUnknown)]
        object QueryService(ref Guid service, ref Guid riid);
    }

    /// <summary>
    /// Opaque handle to a shell application view. We never call its methods,
    /// only pass the pointer between shell APIs, so no vtable is declared.
    /// </summary>
    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("372E1D3B-38D3-42E4-A15B-8AB2B178F513")]
    internal interface IApplicationView
    {
    }

    // ----------------------------------------------------------------- Win10

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("FF72FFDD-BE7E-43FC-9C03-AD81681E88E4")]
    internal interface IVirtualDesktopWin10
    {
        bool IsViewVisible(IApplicationView view);
        Guid GetId();
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("F31574D6-B682-4CDC-BD56-1827860ABEC6")]
    internal interface IVirtualDesktopManagerInternalWin10
    {
        int GetCount();
        void MoveViewToDesktop(IApplicationView view, IVirtualDesktopWin10 desktop);
        bool CanViewMoveDesktops(IApplicationView view);
        IVirtualDesktopWin10 GetCurrentDesktop();
        void GetDesktops(out IObjectArray desktops);
        [PreserveSig]
        int GetAdjacentDesktop(IVirtualDesktopWin10 from, int direction, out IVirtualDesktopWin10 desktop);
        void SwitchDesktop(IVirtualDesktopWin10 desktop);
        IVirtualDesktopWin10 CreateDesktop();
        void RemoveDesktop(IVirtualDesktopWin10 desktop, IVirtualDesktopWin10 fallback);
        IVirtualDesktopWin10 FindDesktop(ref Guid desktopId);
    }

    // ----------------------------------------------------------------- Win11

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("3F07F4BE-B107-441A-AF0F-39D82529072C")]
    internal interface IVirtualDesktopWin11
    {
        bool IsViewVisible(IApplicationView view);
        Guid GetId();
        [return: MarshalAs(UnmanagedType.HString)]
        string GetName();
        [return: MarshalAs(UnmanagedType.HString)]
        string GetWallpaperPath();
        bool IsRemote();
    }

    /// <summary>
    /// Windows 11 21H2/22H2/23H2 (builds 22000–22631): CreateDesktop directly
    /// follows SwitchDesktop; MoveDesktop sits after CreateDesktop.
    /// </summary>
    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("53F5CA0B-158F-4124-900C-057158060B27")]
    internal interface IVirtualDesktopManagerInternalWin11
    {
        int GetCount();
        void MoveViewToDesktop(IApplicationView view, IVirtualDesktopWin11 desktop);
        bool CanViewMoveDesktops(IApplicationView view);
        IVirtualDesktopWin11 GetCurrentDesktop();
        void GetDesktops(out IObjectArray desktops);
        [PreserveSig]
        int GetAdjacentDesktop(IVirtualDesktopWin11 from, int direction, out IVirtualDesktopWin11 desktop);
        void SwitchDesktop(IVirtualDesktopWin11 desktop);
        IVirtualDesktopWin11 CreateDesktop();
        void MoveDesktop(IVirtualDesktopWin11 desktop, int nIndex);
        void RemoveDesktop(IVirtualDesktopWin11 desktop, IVirtualDesktopWin11 fallback);
        IVirtualDesktopWin11 FindDesktop(ref Guid desktopId);
        void GetDesktopSwitchIncludeExcludeViews(IVirtualDesktopWin11 desktop, out IObjectArray unknown1, out IObjectArray unknown2);
        void SetDesktopName(IVirtualDesktopWin11 desktop, [MarshalAs(UnmanagedType.HString)] string name);
        void SetDesktopWallpaper(IVirtualDesktopWin11 desktop, [MarshalAs(UnmanagedType.HString)] string path);
        void UpdateWallpaperPathForAllDesktops([MarshalAs(UnmanagedType.HString)] string path);
        void CopyDesktopState(IApplicationView pView0, IApplicationView pView1);
        void CreateRemoteDesktop([MarshalAs(UnmanagedType.HString)] string path, out IVirtualDesktopWin11 desktop);
        void SwitchRemoteDesktop(IVirtualDesktopWin11 desktop, IntPtr switchType);
        void SwitchDesktopWithAnimation(IVirtualDesktopWin11 desktop);
        void GetLastActiveDesktop(out IVirtualDesktopWin11 desktop);
        void WaitForAnimationToComplete();
    }

    /// <summary>
    /// Windows 11 24H2/25H2 (builds ≥ 26100): same GUID as the older Win11
    /// interface, but SwitchDesktopAndMoveForegroundView was inserted between
    /// SwitchDesktop and CreateDesktop — every method after slot 9 shifted.
    /// </summary>
    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("53F5CA0B-158F-4124-900C-057158060B27")]
    internal interface IVirtualDesktopManagerInternalWin11_24H2
    {
        int GetCount();
        void MoveViewToDesktop(IApplicationView view, IVirtualDesktopWin11 desktop);
        bool CanViewMoveDesktops(IApplicationView view);
        IVirtualDesktopWin11 GetCurrentDesktop();
        void GetDesktops(out IObjectArray desktops);
        [PreserveSig]
        int GetAdjacentDesktop(IVirtualDesktopWin11 from, int direction, out IVirtualDesktopWin11 desktop);
        void SwitchDesktop(IVirtualDesktopWin11 desktop);
        void SwitchDesktopAndMoveForegroundView(IVirtualDesktopWin11 desktop);
        IVirtualDesktopWin11 CreateDesktop();
        void MoveDesktop(IVirtualDesktopWin11 desktop, int nIndex);
        void RemoveDesktop(IVirtualDesktopWin11 desktop, IVirtualDesktopWin11 fallback);
        IVirtualDesktopWin11 FindDesktop(ref Guid desktopId);
        void GetDesktopSwitchIncludeExcludeViews(IVirtualDesktopWin11 desktop, out IObjectArray unknown1, out IObjectArray unknown2);
        void SetDesktopName(IVirtualDesktopWin11 desktop, [MarshalAs(UnmanagedType.HString)] string name);
        void SetDesktopWallpaper(IVirtualDesktopWin11 desktop, [MarshalAs(UnmanagedType.HString)] string path);
        void UpdateWallpaperPathForAllDesktops([MarshalAs(UnmanagedType.HString)] string path);
        void CopyDesktopState(IApplicationView pView0, IApplicationView pView1);
        void CreateRemoteDesktop([MarshalAs(UnmanagedType.HString)] string path, out IVirtualDesktopWin11 desktop);
        void SwitchRemoteDesktop(IVirtualDesktopWin11 desktop, IntPtr switchType);
        void SwitchDesktopWithAnimation(IVirtualDesktopWin11 desktop);
        void GetLastActiveDesktop(out IVirtualDesktopWin11 desktop);
        void WaitForAnimationToComplete();
    }

    /// <summary>
    /// Shared across Windows 10 and 11 (GUID unchanged). Windows 10's real
    /// interface ends at RegisterForApplicationViewChanges; the trailing
    /// Unregister declaration is never invoked there, so declaring it is safe.
    /// </summary>
    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("1841C6D7-4F9D-42C0-AF41-8747538F10E5")]
    internal interface IApplicationViewCollection
    {
        int GetViews(out IObjectArray array);
        int GetViewsByZOrder(out IObjectArray array);
        int GetViewsByAppUserModelId(string id, out IObjectArray array);
        int GetViewForHwnd(IntPtr hwnd, out IApplicationView view);
        int GetViewForApplication(object application, out IApplicationView view);
        int GetViewForAppUserModelId(string id, out IApplicationView view);
        int GetViewInFocus(out IntPtr view);
        int Unknown1(out IntPtr view);
        void RefreshCollection();
        int RegisterForApplicationViewChanges(object listener, out int cookie);
        int UnregisterForApplicationViewChanges(int cookie);
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("92CA9DCD-5622-4BBA-A805-5E9F541BD8C9")]
    internal interface IObjectArray
    {
        void GetCount(out int count);
        void GetAt(int index, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object obj);
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("4CE81583-1E4C-4632-A621-07A53543148F")]
    internal interface IVirtualDesktopPinnedApps
    {
        bool IsAppIdPinned(string appId);
        void PinAppID(string appId);
        void UnpinAppID(string appId);
        bool IsViewPinned(IApplicationView applicationView);
        void PinView(IApplicationView applicationView);
        void UnpinView(IApplicationView applicationView);
    }
}

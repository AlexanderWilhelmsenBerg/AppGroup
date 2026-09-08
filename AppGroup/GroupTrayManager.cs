using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;

namespace AppGroup
{
    public static class GroupTrayManager
    {
        private static int WM_TASKBARCREATED;
        private static IntPtr _hwnd = IntPtr.Zero;
        private static NativeMethods.WndProcDelegate? _wndProcDelegate;
        private static IntPtr _hMenu = IntPtr.Zero;
        private static int _menuActiveGroupSlot = -1;

        private static readonly Dictionary<int, TrayGroup> _icons = new();
        private const uint WM_GROUPTRAY = NativeMethods.WM_TRAYICON + 1;
        private const string WndClassName = "AppGroupGroupTrayWndClass";
        private static uint GroupSlotToUid(int groupSlot) => (uint)(0x1000 + groupSlot);

        public static void SyncFromJson()
        {
            try
            {
                JsonObject root = JsonConfigHelper.ReadCurrentRoot();
                EnsureWindow();
                HashSet<int> incoming = new();

                foreach ((string slotText, JsonObject group) in AppGroupConfigSchema.EnumerateGroups(root))
                {
                    if (!int.TryParse(slotText, out int slot))
                    {
                        continue;
                    }

                    string stableId = group["id"]?.GetValue<string>() ?? string.Empty;
                    string groupName = group["groupName"]?.GetValue<string>() ?? string.Empty;
                    string groupIcon = group["groupIcon"]?.GetValue<string>() ?? string.Empty;
                    bool showOnTray = group["showOnTray"]?.GetValue<bool>() ?? false;
                    if (!showOnTray || string.IsNullOrWhiteSpace(stableId) || string.IsNullOrWhiteSpace(groupName))
                    {
                        continue;
                    }

                    incoming.Add(slot);
                    AddGroup(new TrayGroup(slot, stableId, groupName, groupIcon));
                }

                foreach (int slot in new List<int>(_icons.Keys))
                {
                    if (!incoming.Contains(slot))
                    {
                        RemoveGroup(slot);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GroupTrayManager.SyncFromJson failed: {ex.Message}");
            }
        }

        public static void Cleanup()
        {
            RemoveAll();
            if (_hMenu != IntPtr.Zero)
            {
                NativeMethods.DestroyMenu(_hMenu);
                _hMenu = IntPtr.Zero;
            }
        }

        private static void EnsureWindow()
        {
            if (_hwnd != IntPtr.Zero)
            {
                return;
            }

            _wndProcDelegate = WndProc;
            NativeMethods.WNDCLASSEX windowClass = new()
            {
                cbSize = (uint)Marshal.SizeOf<NativeMethods.WNDCLASSEX>(),
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate),
                hInstance = NativeMethods.GetModuleHandle(null),
                hCursor = NativeMethods.LoadCursor(IntPtr.Zero, 32512u),
                lpszClassName = WndClassName
            };
            NativeMethods.RegisterClassEx(ref windowClass);
            WM_TASKBARCREATED = NativeMethods.RegisterWindowMessage("TaskbarCreated");
            _hwnd = NativeMethods.CreateWindowEx(
                0, WndClassName, "AppGroup GroupTray", 0,
                0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero,
                NativeMethods.GetModuleHandle(null), IntPtr.Zero);
        }

        private static void AddGroup(TrayGroup group)
        {
            bool existing = _icons.ContainsKey(group.Slot);
            if (_icons.TryGetValue(group.Slot, out TrayGroup? old))
            {
                if (old.IconHandle != IntPtr.Zero)
                {
                    NativeMethods.DestroyIcon(old.IconHandle);
                }
                _icons.Remove(group.Slot);
            }

            IntPtr iconHandle = LoadGroupIcon(group.IconPath);
            NativeMethods.NOTIFYICONDATA data = BuildNid(GroupSlotToUid(group.Slot), iconHandle, group.DisplayName);
            bool ok = NativeMethods.Shell_NotifyIcon(existing ? NativeMethods.NIM_MODIFY : NativeMethods.NIM_ADD, ref data);
            if (!ok)
            {
                ok = NativeMethods.Shell_NotifyIcon(existing ? NativeMethods.NIM_ADD : NativeMethods.NIM_MODIFY, ref data);
            }
            if (!ok)
            {
                if (iconHandle != IntPtr.Zero)
                {
                    NativeMethods.DestroyIcon(iconHandle);
                }
                return;
            }

            _icons[group.Slot] = group with { IconHandle = iconHandle };
        }

        private static void RemoveGroup(int slot)
        {
            NativeMethods.NOTIFYICONDATA data = new()
            {
                cbSize = Marshal.SizeOf<NativeMethods.NOTIFYICONDATA>(),
                hWnd = _hwnd,
                uID = GroupSlotToUid(slot)
            };
            NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_DELETE, ref data);

            if (_icons.TryGetValue(slot, out TrayGroup? entry))
            {
                if (entry.IconHandle != IntPtr.Zero)
                {
                    NativeMethods.DestroyIcon(entry.IconHandle);
                }
                _icons.Remove(slot);
            }
        }

        private static void RemoveAll()
        {
            foreach (int slot in new List<int>(_icons.Keys))
            {
                RemoveGroup(slot);
            }
        }

        private static IntPtr LoadGroupIcon(string iconPath)
        {
            if (!string.IsNullOrWhiteSpace(iconPath))
            {
                string extension = Path.GetExtension(iconPath).ToLowerInvariant();
                if (extension is ".png" or ".jpg" or ".jpeg")
                {
                    string? directory = Path.GetDirectoryName(iconPath);
                    if (!string.IsNullOrEmpty(directory))
                    {
                        string icoPath = Path.Combine(directory, Path.GetFileNameWithoutExtension(iconPath) + ".ico");
                        if (File.Exists(icoPath))
                        {
                            iconPath = icoPath;
                        }
                    }
                }

                if (File.Exists(iconPath))
                {
                    IntPtr handle = NativeMethods.LoadImage(IntPtr.Zero, iconPath,
                        NativeMethods.IMAGE_ICON, 16, 16, NativeMethods.LR_LOADFROMFILE);
                    if (handle != IntPtr.Zero)
                    {
                        return handle;
                    }
                }
            }

            return NativeMethods.LoadImage(IntPtr.Zero, "#32516", NativeMethods.IMAGE_ICON, 16, 16, 0);
        }

        private static NativeMethods.NOTIFYICONDATA BuildNid(uint uid, IntPtr iconHandle, string tip) =>
            new()
            {
                cbSize = Marshal.SizeOf<NativeMethods.NOTIFYICONDATA>(),
                hWnd = _hwnd,
                uID = uid,
                uFlags = NativeMethods.NIF_MESSAGE | NativeMethods.NIF_ICON | NativeMethods.NIF_TIP,
                uCallbackMessage = WM_GROUPTRAY,
                hIcon = iconHandle,
                szTip = tip.Length > 127 ? tip[..127] : tip
            };

        private static IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            if ((int)msg == WM_TASKBARCREATED)
            {
                foreach (TrayGroup group in _icons.Values)
                {
                    if (group.IconHandle != IntPtr.Zero)
                    {
                        NativeMethods.DestroyIcon(group.IconHandle);
                    }
                }
                _icons.Clear();
                SyncFromJson();
                return IntPtr.Zero;
            }

            if (msg == WM_GROUPTRAY)
            {
                int slot = wParam.ToInt32() - 0x1000;
                int mouseMessage = lParam.ToInt32();
                if ((mouseMessage == 0x0202 || mouseMessage == 0x0203) && _icons.TryGetValue(slot, out TrayGroup? group))
                {
                    Launch(JsonConfigHelper.BuildGroupActivationArguments(group.StableId));
                }

                if (mouseMessage == 0x0205)
                {
                    _menuActiveGroupSlot = slot;
                    ShowContextMenu();
                }
                return IntPtr.Zero;
            }

            return NativeMethods.DefWindowProc(hWnd, msg, wParam, lParam);
        }

        private static void ShowContextMenu()
        {
            if (_hMenu != IntPtr.Zero)
            {
                NativeMethods.DestroyMenu(_hMenu);
            }
            _hMenu = NativeMethods.CreatePopupMenu();
            NativeMethods.AppendMenu(_hMenu, 0, 1, "Edit this Group");
            NativeMethods.AppendMenu(_hMenu, 0, 2, "Launch All");

            NativeMethods.GetCursorPos(out NativeMethods.POINT point);
            NativeMethods.SetForegroundWindow(_hwnd);
            uint result = NativeMethods.TrackPopupMenu(
                _hMenu,
                NativeMethods.TPM_RETURNCMD | NativeMethods.TPM_RIGHTBUTTON,
                point.X, point.Y, 0, _hwnd, IntPtr.Zero);
            NativeMethods.PostMessage(_hwnd, NativeMethods.WM_NULL, IntPtr.Zero, IntPtr.Zero);

            if (!_icons.TryGetValue(_menuActiveGroupSlot, out TrayGroup? group))
            {
                return;
            }

            if (result == 1)
            {
                Launch(JsonConfigHelper.BuildEditActivationArguments(group.StableId));
            }
            else if (result == 2)
            {
                Launch(JsonConfigHelper.BuildLaunchAllArguments(group.StableId));
            }
        }

        private static void Launch(string arguments)
        {
            try
            {
                string executable = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AppGroup.exe");
                Process.Start(new ProcessStartInfo
                {
                    FileName = executable,
                    Arguments = arguments,
                    UseShellExecute = false
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GroupTrayManager: launch failed '{arguments}': {ex.Message}");
            }
        }

        private sealed record TrayGroup(int Slot, string StableId, string DisplayName, string IconPath, IntPtr IconHandle = default);
    }
}

using IWshRuntimeLibrary;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace AppGroup
{
    public class TaskbarManager
    {
        private static readonly SemaphoreSlim Semaphore = new(1, 1);
        private static readonly object CacheLock = new();
        private static string? _cachedTaskbarPath;
        private static string? _cachedExePath;

        public static async Task<bool> IsShortcutPinnedToTaskbar(string stableGroupId)
        {
            return await IsShortcutPinnedToTaskbar(stableGroupId, null);
        }

        public static async Task<bool> IsShortcutPinnedToTaskbar(string stableGroupId, string? legacyGroupName)
        {
            return await Task.Run(() =>
            {
                try
                {
                    string taskbarPath = GetTaskbarPath();
                    if (!Directory.Exists(taskbarPath))
                    {
                        return false;
                    }

                    WshShell shell = new();
                    foreach (string shortcutFile in Directory.EnumerateFiles(taskbarPath, "*.lnk"))
                    {
                        IWshShortcut? shortcut = null;
                        try
                        {
                            shortcut = (IWshShortcut)shell.CreateShortcut(shortcutFile);
                            if (ShortcutMatches(shortcut, stableGroupId, legacyGroupName))
                            {
                                return true;
                            }
                        }
                        catch
                        {
                        }
                        finally
                        {
                            if (shortcut is not null)
                            {
                                Marshal.ReleaseComObject(shortcut);
                            }
                        }
                    }
                    return false;
                }
                catch
                {
                    return false;
                }
            });
        }

        public static async Task UpdateTaskbarShortcutIcon(string stableGroupId, string iconPath)
        {
            await UpdateTaskbarShortcutIcon(stableGroupId, null, null, iconPath);
        }

        public static async Task UpdateTaskbarShortcutIcon(
            string stableGroupId,
            string? legacyGroupName,
            string? displayName,
            string iconPath)
        {
            await Task.Run(() =>
            {
                try
                {
                    string taskbarPath = GetTaskbarPath();
                    if (!Directory.Exists(taskbarPath))
                    {
                        return;
                    }

                    string executablePath = GetExecutablePath();
                    WshShell shell = new();
                    foreach (string shortcutFile in Directory.EnumerateFiles(taskbarPath, "*.lnk"))
                    {
                        IWshShortcut? shortcut = null;
                        try
                        {
                            shortcut = (IWshShortcut)shell.CreateShortcut(shortcutFile);
                            if (!string.Equals(shortcut.TargetPath, executablePath, StringComparison.OrdinalIgnoreCase) ||
                                !ShortcutMatches(shortcut, stableGroupId, legacyGroupName))
                            {
                                continue;
                            }

                            shortcut.Arguments = JsonConfigHelper.BuildGroupActivationArguments(stableGroupId);
                            shortcut.Description = $"{stableGroupId} - AppGroup Shortcut";
                            shortcut.IconLocation = iconPath;
                            shortcut.Save();
                            TryRefreshTaskbarWithoutRestartAsync();
                            return;
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error processing shortcut {shortcutFile}: {ex.Message}");
                        }
                        finally
                        {
                            if (shortcut is not null)
                            {
                                Marshal.ReleaseComObject(shortcut);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error updating taskbar shortcut: {ex.Message}");
                }
            });
        }

        public static async Task UpdateTaskbarShortcutIcon(string oldGroupName, string newGroupName, string iconPath)
        {
            try
            {
                string stableId = JsonConfigHelper.ResolveStableGroupId(oldGroupName, allowLegacyName: true);
                await UpdateTaskbarShortcutIcon(stableId, oldGroupName, newGroupName, iconPath);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Legacy taskbar shortcut update could not resolve '{oldGroupName}': {ex.Message}");
            }
        }

        public static void UpdateTaskbarShortcutIconAsync(string stableGroupId, string iconPath)
        {
            _ = Task.Run(async () =>
            {
                await UpdateTaskbarShortcutIcon(stableGroupId, iconPath);
                await Task.Delay(200);
                TryRefreshTaskbarWithoutRestartAsync();
            });
        }

        public static void TryRefreshTaskbarWithoutRestartAsync()
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    NativeMethods.SHChangeNotify(NativeMethods.SHCNE_ASSOCCHANGED, NativeMethods.SHCNF_FLUSH, IntPtr.Zero, IntPtr.Zero);
                    await Task.Delay(100);
                    IntPtr taskbarWindow = NativeMethods.FindWindow("Shell_TrayWnd", null);
                    if (taskbarWindow != IntPtr.Zero)
                    {
                        NativeMethods.InvalidateRect(taskbarWindow, IntPtr.Zero, true);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error in refresh attempt: {ex.Message}");
                }
            });
        }

        public static async Task TryRefreshTaskbarWithoutRestart()
        {
            await Task.Run(() =>
            {
                try
                {
                    NativeMethods.SHChangeNotify(NativeMethods.SHCNE_ASSOCCHANGED, NativeMethods.SHCNF_FLUSH, IntPtr.Zero, IntPtr.Zero);
                    IntPtr taskbarWindow = NativeMethods.FindWindow("Shell_TrayWnd", null);
                    if (taskbarWindow != IntPtr.Zero)
                    {
                        NativeMethods.InvalidateRect(taskbarWindow, IntPtr.Zero, true);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error in refresh attempt: {ex.Message}");
                }
            });
        }

        public static void ForceTaskbarUpdateAsync()
        {
            _ = Task.Run(async () =>
            {
                if (!await Semaphore.WaitAsync(100))
                {
                    return;
                }

                try
                {
                    using Process killProcess = new()
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = "taskkill",
                            Arguments = "/f /im explorer.exe",
                            WindowStyle = ProcessWindowStyle.Hidden,
                            CreateNoWindow = true,
                            UseShellExecute = false
                        }
                    };
                    killProcess.Start();
                    await killProcess.WaitForExitAsync();
                    await Task.Delay(300);
                    Process.Start("explorer.exe");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error restarting explorer: {ex.Message}");
                }
                finally
                {
                    Semaphore.Release();
                }
            });
        }

        private static bool ShortcutMatches(IWshShortcut shortcut, string stableGroupId, string? legacyGroupName)
        {
            string arguments = shortcut.Arguments ?? string.Empty;
            if (ContainsStableGroupId(arguments, stableGroupId))
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(legacyGroupName) &&
                (string.Equals(arguments.Trim().Trim('"'), legacyGroupName, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(shortcut.Description, $"{legacyGroupName} - AppGroup Shortcut", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            return false;
        }

        private static bool ContainsStableGroupId(string arguments, string stableGroupId)
        {
            string trimmed = arguments.Trim();
            if (string.Equals(trimmed.Trim('"'), stableGroupId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (trimmed.StartsWith("--group=", StringComparison.OrdinalIgnoreCase))
            {
                return string.Equals(trimmed[8..].Trim().Trim('"'), stableGroupId, StringComparison.OrdinalIgnoreCase);
            }

            if (trimmed.StartsWith("--group ", StringComparison.OrdinalIgnoreCase))
            {
                return string.Equals(trimmed[8..].Trim().Trim('"'), stableGroupId, StringComparison.OrdinalIgnoreCase);
            }

            return false;
        }

        private static string GetTaskbarPath()
        {
            if (_cachedTaskbarPath is not null)
            {
                return _cachedTaskbarPath;
            }

            lock (CacheLock)
            {
                _cachedTaskbarPath ??= Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar");
                return _cachedTaskbarPath;
            }
        }

        private static string GetExecutablePath()
        {
            if (_cachedExePath is not null)
            {
                return _cachedExePath;
            }

            lock (CacheLock)
            {
                _cachedExePath ??= Process.GetCurrentProcess().MainModule?.FileName
                    ?? Path.Combine(Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory, "AppGroup.exe");
                return _cachedExePath;
            }
        }
    }
}

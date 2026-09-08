using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Windows.UI.StartScreen;

namespace AppGroup
{
    public class Program
    {
        public static NativeMethods.POINT InitialClickPos;
        public static string? InitialStableGroupId { get; private set; }

        [STAThread]
        static void Main(string[] args)
        {
            NativeMethods.GetCursorPos(out InitialClickPos);
            string[] cmdArgs = Environment.GetCommandLineArgs();
            bool isSilent = HasSilentFlag(cmdArgs);

            try
            {
                JsonConfigHelper.EnsureCurrentSchema();
            }
            catch (ConfigMigrationException ex)
            {
                ShowActivationError("AppGroup could not migrate its configuration safely. The original file was left unchanged.\n\n" + ex.Message);
                return;
            }

            if (cmdArgs.Length <= 1 && !isSilent)
            {
                IntPtr existingMainHWnd = NativeMethods.FindWindow(null, "App Group");
                if (existingMainHWnd != IntPtr.Zero)
                {
                    NativeMethods.SendString(existingMainHWnd, "__SHOW_MAIN__");
                    return;
                }
            }

            if (!isSilent && cmdArgs.Length > 1)
            {
                string command = cmdArgs[1];
                IntPtr existingPopupHWnd = NativeMethods.FindWindow(null, "Popup Window");
                IntPtr existingEditHWnd = NativeMethods.FindWindow(null, "Edit Group");
                IntPtr existingMainHWnd = NativeMethods.FindWindow(null, "App Group");

                if (string.Equals(command, "EditGroupWindow", StringComparison.OrdinalIgnoreCase))
                {
                    string? stableId = ExtractStableGroupSelector(cmdArgs, allowLegacyName: false);
                    int groupSlot = ResolveEditSlot(cmdArgs, stableId);
                    SaveGroupIdToFile(groupSlot.ToString(CultureInfo.InvariantCulture));

                    if (existingEditHWnd != IntPtr.Zero)
                    {
                        EditGroupHelper editGroup = new("Edit Group", groupSlot);
                        editGroup.Activate();
                        return;
                    }
                    if (existingMainHWnd != IntPtr.Zero || existingPopupHWnd != IntPtr.Zero)
                    {
                        return;
                    }
                }
                else if (string.Equals(command, "LaunchAll", StringComparison.OrdinalIgnoreCase))
                {
                    string? selector = ExtractStableGroupSelector(cmdArgs, allowLegacyName: true);
                    if (string.IsNullOrWhiteSpace(selector))
                    {
                        ShowActivationError("The Launch All command does not identify a valid group.");
                        return;
                    }

                    GroupResolution launchResolution = JsonConfigHelper.ResolveGroup(selector, allowLegacyName: true);
                    if (!TryAcceptResolution(launchResolution, selector, out string stableId))
                    {
                        return;
                    }

                    Task.Run(() => JsonConfigHelper.LaunchAll(stableId));
                    InitializeJumpListSync(stableId);
                    return;
                }
                else
                {
                    string? selector = ExtractNormalGroupSelector(cmdArgs);
                    if (string.IsNullOrWhiteSpace(selector))
                    {
                        ShowActivationError("The group shortcut does not contain a valid group identifier.");
                        return;
                    }

                    GroupResolution resolution = JsonConfigHelper.ResolveGroup(selector, allowLegacyName: true);
                    if (!TryAcceptResolution(resolution, selector, out string stableId))
                    {
                        return;
                    }

                    InitialStableGroupId = stableId;
                    int groupSlot = int.Parse(resolution.Group!.Slot, CultureInfo.InvariantCulture);
                    SaveGroupIdToFile(groupSlot.ToString(CultureInfo.InvariantCulture));

                    if (existingPopupHWnd != IntPtr.Zero)
                    {
                        NativeMethods.SendString(existingPopupHWnd, stableId);
                        NativeMethods.ForceForegroundWindow(existingPopupHWnd);
                        InitializeJumpListSync(stableId);
                        return;
                    }
                }
            }

            WinRT.ComWrappersSupport.InitializeComWrappers();
            Application.Start(_ =>
            {
                DispatcherQueueSynchronizationContext context = new(DispatcherQueue.GetForCurrentThread());
                SynchronizationContext.SetSynchronizationContext(context);
                _ = new App();
            });
        }

        private static bool TryAcceptResolution(GroupResolution resolution, string selector, out string stableId)
        {
            stableId = string.Empty;
            if (resolution.Status == GroupResolutionStatus.Found && resolution.Group is not null)
            {
                stableId = resolution.Group.StableId;
                return true;
            }

            if (resolution.Status == GroupResolutionStatus.Ambiguous)
            {
                ShowActivationError(
                    $"More than one AppGroup group is named '{selector}'.\n\n" +
                    "This legacy name-based shortcut is ambiguous and was not opened. Open AppGroup and re-pin the intended group so the shortcut uses its stable ID.");
            }
            else
            {
                ShowActivationError(
                    $"AppGroup could not find the group '{selector}'.\n\n" +
                    "The shortcut may be stale. Open AppGroup and pin the group again.");
            }
            return false;
        }

        private static int ResolveEditSlot(string[] args, string? stableId)
        {
            if (!string.IsNullOrWhiteSpace(stableId))
            {
                return JsonConfigHelper.FindKeyByStableGroupId(stableId);
            }

            foreach (string arg in args)
            {
                if (arg.StartsWith("--id=", StringComparison.OrdinalIgnoreCase) &&
                    int.TryParse(arg[5..], NumberStyles.None, CultureInfo.InvariantCulture, out int legacySlot))
                {
                    return legacySlot;
                }
            }

            throw new KeyNotFoundException("The Edit Group command does not identify an existing group.");
        }

        private static string? ExtractNormalGroupSelector(string[] args)
        {
            string? typed = ExtractStableGroupSelector(args, allowLegacyName: false);
            if (!string.IsNullOrWhiteSpace(typed))
            {
                return typed;
            }

            if (args.Length > 1 && !args[1].StartsWith("--", StringComparison.Ordinal))
            {
                return args[1];
            }

            return null;
        }

        private static string? ExtractStableGroupSelector(string[] args, bool allowLegacyName)
        {
            for (int index = 1; index < args.Length; index++)
            {
                string arg = args[index];
                if (arg.StartsWith("--group=", StringComparison.OrdinalIgnoreCase))
                {
                    return arg[8..].Trim('"');
                }
                if (string.Equals(arg, "--group", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
                {
                    return args[index + 1].Trim('"');
                }
                if (allowLegacyName && arg.StartsWith("--groupName=", StringComparison.OrdinalIgnoreCase))
                {
                    return arg[12..].Trim('"');
                }
                if (allowLegacyName && arg.StartsWith("--groupId=", StringComparison.OrdinalIgnoreCase) &&
                    int.TryParse(arg[10..], NumberStyles.None, CultureInfo.InvariantCulture, out int legacySlot))
                {
                    string stableId = JsonConfigHelper.FindStableIdByKey(legacySlot);
                    return string.IsNullOrWhiteSpace(stableId) ? null : stableId;
                }
            }
            return null;
        }

        private static void InitializeJumpListSync(string stableGroupId)
        {
            try
            {
                Task.Run(() => InitializeJumpListAsync(stableGroupId)).Wait();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Sync jump list initialization failed: {ex.Message}");
            }
        }

        private static async Task InitializeJumpListAsync(string stableGroupId)
        {
            try
            {
                JumpList jumpList = await JumpList.LoadCurrentAsync();
                jumpList.Items.Clear();
                jumpList.Items.Add(JumpListItem.CreateWithArguments(
                    JsonConfigHelper.BuildEditActivationArguments(stableGroupId),
                    "Edit this Group"));
                jumpList.Items.Add(JumpListItem.CreateWithArguments(
                    JsonConfigHelper.BuildLaunchAllArguments(stableGroupId),
                    "Launch All"));
                await jumpList.SaveAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Jump list initialization failed: {ex.Message}");
            }
        }

        private static void SaveGroupIdToFile(string groupId)
        {
            try
            {
                string filePath = Path.Combine(AppPaths.BaseDataPath, "lastEdit");
                Directory.CreateDirectory(Path.GetDirectoryName(filePath) ?? AppPaths.BaseDataPath);
                File.WriteAllText(filePath, groupId);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to save group ID: {ex.Message}");
            }
        }

        private static bool HasSilentFlag(string[] args)
        {
            return args.Any(arg => string.Equals(arg, "--silent", StringComparison.OrdinalIgnoreCase));
        }

        private static void ShowActivationError(string message)
        {
            Debug.WriteLine(message);
            MessageBox(IntPtr.Zero, message, "AppGroup", 0x00000030u);
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = false)]
        private static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);
    }
}

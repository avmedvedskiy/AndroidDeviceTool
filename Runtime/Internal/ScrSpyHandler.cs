using System;
using System.Diagnostics;
using System.IO;
using UnityEditor.PackageManager;

internal static class ScrSpyHandler
{
    private const string PACKAGE_NAME = "com.avmedvedskiy.scrspy";
    private const string PACKAGE_ANCHOR_ASSET_PATH = "Packages/com.avmedvedskiy.scrspy/package.json";

    public static bool TryGetToolPaths(out string workingDirectory, out string adbPath, out string scrcpyPath, out string error)
    {
        workingDirectory = null;
        adbPath = null;
        scrcpyPath = null;
        error = null;

        var packageInfo = PackageInfo.FindForAssetPath(PACKAGE_ANCHOR_ASSET_PATH);
        if (packageInfo == null || string.IsNullOrEmpty(packageInfo.resolvedPath))
        {
            error = "Unable to resolve package path for '" + PACKAGE_NAME + "' via '" + PACKAGE_ANCHOR_ASSET_PATH + "'.";
            return false;
        }

        workingDirectory = Path.Combine(packageInfo.resolvedPath, "scrspy");
        adbPath = Path.Combine(workingDirectory, "adb.exe");
        scrcpyPath = Path.Combine(workingDirectory, "scrcpy.exe");

        if (!Directory.Exists(workingDirectory))
        {
            error = "scrspy directory not found: " + workingDirectory;
            return false;
        }

        if (!File.Exists(adbPath))
        {
            error = "adb.exe not found: " + adbPath;
            return false;
        }

        if (!File.Exists(scrcpyPath))
        {
            error = "scrcpy.exe not found: " + scrcpyPath;
            return false;
        }

        return true;
    }

    public static void StartScreenShare(string scrcpyPath, string adbPath, string workingDirectory, string deviceId = null)
    {
        var arguments = string.IsNullOrWhiteSpace(deviceId) ? string.Empty : "-s \"" + deviceId + "\" ";
        arguments += "--stay-awake";

        var process = StartProcess(scrcpyPath, adbPath, workingDirectory, arguments);
        if (process == null)
            throw new InvalidOperationException("Unable to start scrcpy: " + scrcpyPath);
    }

    public static Process StartSessionRecording(string scrcpyPath, string adbPath, string workingDirectory, string videoFilePath, string deviceId)
    {
        var arguments = string.Empty;
        if (!string.IsNullOrWhiteSpace(deviceId))
            arguments += "-s \"" + deviceId + "\" ";

        arguments += "--stay-awake ";
        arguments += "--record \"" + videoFilePath + "\"";

        var process = StartProcess(scrcpyPath, adbPath, workingDirectory, arguments);
        if (process == null)
            throw new InvalidOperationException("Unable to start session recording with scrcpy: " + scrcpyPath);

        return process;
    }

    private static Process StartProcess(string scrcpyPath, string adbPath, string workingDirectory, string arguments)
    {
        var info = new ProcessStartInfo
        {
            FileName = scrcpyPath,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            Arguments = arguments ?? string.Empty
        };

        info.EnvironmentVariables["ADB"] = adbPath;
        return Process.Start(info);
    }
}

using System;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

public static class AndroidInstallTool
{
    private const string MenuRoot = "Tools/BuildTools/Android/";

    [MenuItem(MenuRoot + "Install Selected APK And Run scrcpy")]
    public static void InstallSelectedApkAndRunScrcpy()
    {
        var initialDir = GetInitialApkDirectory();
        var apkPath = EditorUtility.OpenFilePanel("Select APK", initialDir, "apk");
        if (string.IsNullOrEmpty(apkPath))
            return;

        SettingsStorage.SetLastApkPath(apkPath);
        InstallAndRun(apkPath);
    }

    private static void InstallAndRun(string apkPath)
    {
        if (!ScrSpyHandler.TryGetToolPaths(out var scrcpyDir, out var adbPath, out var scrcpyPath, out var error))
        {
            Debug.LogError(error);
            return;
        }

        if (!File.Exists(apkPath))
        {
            Debug.LogError("APK not found: " + apkPath);
            return;
        }

        if (!AaptHandler.TryGetPackageName(apkPath, out var packageName, out var packageError))
        {
            Debug.LogError(packageError);
            return;
        }

        var adbHandler = new AdbHandler(adbPath, scrcpyDir);

        try
        {
            Debug.Log("Using APK: " + apkPath);
            Debug.Log("Resolved package name: " + packageName);

            adbHandler.StartServer();
            adbHandler.WaitForDevice();
            adbHandler.InstallApkWithFallback(apkPath, packageName);
            adbHandler.UnlockDeviceAndSwipeUp();
            adbHandler.LaunchInstalledApp(packageName);
            AndroidLogcatHandler.OpenAndroidLogcatForCurrentApp(packageName, adbHandler.TryGetTargetDeviceId());

            StartScrcpy(scrcpyPath, adbPath, scrcpyDir);
            Debug.Log("APK installed, app launched, Android Logcat opened and scrcpy started.");
        }
        catch (Exception exception)
        {
            Debug.LogError("Install/launch failed:\n" + exception.Message);
        }
    }

    private static void StartScrcpy(string scrcpyPath, string adbPath, string workingDirectory)
    {
        var info = new ProcessStartInfo
        {
            FileName = scrcpyPath,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        info.EnvironmentVariables["ADB"] = adbPath;

        Process.Start(info);
    }

    private static string GetInitialApkDirectory()
    {
        var lastApkPath = SettingsStorage.GetLastApkPath();
        if (!string.IsNullOrWhiteSpace(lastApkPath))
            return Path.GetDirectoryName(lastApkPath);

        var projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
        return string.IsNullOrEmpty(projectRoot) ? Application.dataPath : projectRoot;
    }
}

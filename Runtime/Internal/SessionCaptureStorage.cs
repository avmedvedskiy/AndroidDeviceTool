using System;
using System.IO;
using UnityEngine;

internal static class SessionCaptureStorage
{
    private const string ReportsRootFolderName = "SessionCaptures";
    private const string LogFileName = "log.txt";
    private const string UnityLogFileName = "log_unity.txt";
    private const string VideoFileName = "video.mp4";

    public static SessionCapturePaths CreateSessionPaths(AdbDeviceInfo device, DateTime? sessionStartLocalTime = null)
    {
        var startTime = sessionStartLocalTime ?? DateTime.Now;

        var reportsRoot = GetReportsRootDirectory();
        var deviceFolder = BuildDeviceFolderName(device);
        var timestampFolder = startTime.ToString("yyyy-MM-dd_HH-mm-ss");

        var sessionDirectory = Path.Combine(reportsRoot, deviceFolder, timestampFolder);
        sessionDirectory = MakeUniqueDirectoryPath(sessionDirectory);
        Directory.CreateDirectory(sessionDirectory);

        return new SessionCapturePaths(
            reportsRoot,
            Path.Combine(reportsRoot, deviceFolder),
            sessionDirectory,
            Path.Combine(sessionDirectory, LogFileName),
            Path.Combine(sessionDirectory, UnityLogFileName),
            Path.Combine(sessionDirectory, VideoFileName),
            startTime);
    }

    public static string GetDeviceSessionsDirectory(AdbDeviceInfo device, bool createIfMissing)
    {
        var reportsRoot = GetReportsRootDirectory();
        var deviceFolder = BuildDeviceFolderName(device);
        var deviceDirectory = Path.Combine(reportsRoot, deviceFolder);

        if (createIfMissing)
            Directory.CreateDirectory(deviceDirectory);

        return deviceDirectory;
    }

    private static string GetReportsRootDirectory()
    {
        var persistentPath = Application.persistentDataPath;
        if (string.IsNullOrWhiteSpace(persistentPath))
            persistentPath = Application.dataPath;

        return Path.Combine(persistentPath, ReportsRootFolderName);
    }

    private static string BuildDeviceFolderName(AdbDeviceInfo device)
    {
        if (!string.IsNullOrWhiteSpace(device.Model))
            return SanitizeFileName(device.Model);

        if (!string.IsNullOrWhiteSpace(device.Device))
            return SanitizeFileName(device.Device);

        if (!string.IsNullOrWhiteSpace(device.Product))
            return SanitizeFileName(device.Product);

        if (!string.IsNullOrWhiteSpace(device.Id))
            return SanitizeFileName(device.Id);

        return "UnknownDevice";
    }

    private static string SanitizeFileName(string value)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = value.Trim();

        foreach (var invalidChar in invalidChars)
            sanitized = sanitized.Replace(invalidChar, '_');

        return string.IsNullOrWhiteSpace(sanitized) ? "UnknownDevice" : sanitized;
    }

    private static string MakeUniqueDirectoryPath(string basePath)
    {
        if (!Directory.Exists(basePath))
            return basePath;

        var suffix = 1;
        while (true)
        {
            var candidate = basePath + "_" + suffix.ToString("00");
            if (!Directory.Exists(candidate))
                return candidate;

            suffix++;
        }
    }
}

internal readonly struct SessionCapturePaths
{
    public readonly string ReportsRootDirectory;
    public readonly string DeviceDirectory;
    public readonly string SessionDirectory;
    public readonly string LogFilePath;
    public readonly string UnityLogFilePath;
    public readonly string VideoFilePath;
    public readonly DateTime SessionStartLocalTime;

    public SessionCapturePaths(
        string reportsRootDirectory,
        string deviceDirectory,
        string sessionDirectory,
        string logFilePath,
        string unityLogFilePath,
        string videoFilePath,
        DateTime sessionStartLocalTime)
    {
        ReportsRootDirectory = reportsRootDirectory;
        DeviceDirectory = deviceDirectory;
        SessionDirectory = sessionDirectory;
        LogFilePath = logFilePath;
        UnityLogFilePath = unityLogFilePath;
        VideoFilePath = videoFilePath;
        SessionStartLocalTime = sessionStartLocalTime;
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

internal static class SettingsStorage
{
    private const string LAST_APK_PATH_KEY = "com.avmedvedskiy.scrspy.last_apk_path";
    private const string LAST_BUNDLE_NAME_KEY = "com.avmedvedskiy.scrspy.last_bundle_name";
    private const string PROJECT_SETTINGS_FILE_NAME = "ScrspySettings.json";
    private const int MAX_BUNDLE_NAME_HISTORY_COUNT = 20;

    public static void SetLastApkPath(string apkPath)
    {
        if (string.IsNullOrWhiteSpace(apkPath))
            return;

        var fullPath = Path.GetFullPath(apkPath);
        EditorPrefs.SetString(LAST_APK_PATH_KEY, fullPath);
    }

    public static string GetLastApkPath()
    {
        var storedPath = EditorPrefs.GetString(LAST_APK_PATH_KEY, string.Empty);
        if (string.IsNullOrWhiteSpace(storedPath))
            return null;

        var fullPath = Path.GetFullPath(storedPath);
        return File.Exists(fullPath) ? fullPath : null;
    }

    public static void SetLastBundleName(string bundleName)
    {
        if (string.IsNullOrWhiteSpace(bundleName))
        {
            EditorPrefs.DeleteKey(LAST_BUNDLE_NAME_KEY);
            return;
        }

        EditorPrefs.SetString(LAST_BUNDLE_NAME_KEY, bundleName.Trim());
    }

    public static string GetLastBundleName()
    {
        var bundleName = EditorPrefs.GetString(LAST_BUNDLE_NAME_KEY, string.Empty);
        return string.IsNullOrWhiteSpace(bundleName) ? null : bundleName.Trim();
    }

    public static IReadOnlyList<string> GetBundleNameHistory()
    {
        var data = LoadProjectSettings();
        return data.BundleNameHistory
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static void AddBundleNameHistory(string bundleName)
    {
        var normalized = string.IsNullOrWhiteSpace(bundleName) ? null : bundleName.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            return;

        var data = LoadProjectSettings();
        data.BundleNameHistory.RemoveAll(value => string.Equals(value, normalized, StringComparison.OrdinalIgnoreCase));
        data.BundleNameHistory.Insert(0, normalized);

        if (data.BundleNameHistory.Count > MAX_BUNDLE_NAME_HISTORY_COUNT)
            data.BundleNameHistory.RemoveRange(MAX_BUNDLE_NAME_HISTORY_COUNT, data.BundleNameHistory.Count - MAX_BUNDLE_NAME_HISTORY_COUNT);

        SaveProjectSettings(data);
    }

    public static void RemoveBundleNameHistory(string bundleName)
    {
        var normalized = string.IsNullOrWhiteSpace(bundleName) ? null : bundleName.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            return;

        var data = LoadProjectSettings();
        if (data.BundleNameHistory.RemoveAll(value => string.Equals(value, normalized, StringComparison.OrdinalIgnoreCase)) > 0)
            SaveProjectSettings(data);
    }

    private static ProjectSettingsData LoadProjectSettings()
    {
        var filePath = GetProjectSettingsFilePath();
        if (!File.Exists(filePath))
            return new ProjectSettingsData();

        try
        {
            var json = File.ReadAllText(filePath, Encoding.UTF8);
            var data = JsonUtility.FromJson<ProjectSettingsData>(json);
            return data ?? new ProjectSettingsData();
        }
        catch
        {
            return new ProjectSettingsData();
        }
    }

    private static void SaveProjectSettings(ProjectSettingsData data)
    {
        var filePath = GetProjectSettingsFilePath();
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
            Directory.CreateDirectory(directory);

        var json = JsonUtility.ToJson(data ?? new ProjectSettingsData(), prettyPrint: true);
        File.WriteAllText(filePath, json + Environment.NewLine, Encoding.UTF8);
    }

    private static string GetProjectSettingsFilePath()
    {
        var projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
        return Path.Combine(projectRoot, "ProjectSettings", PROJECT_SETTINGS_FILE_NAME);
    }

    [Serializable]
    private sealed class ProjectSettingsData
    {
        public List<string> BundleNameHistory = new List<string>();
    }
}

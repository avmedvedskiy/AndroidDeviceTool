using System.IO;
using UnityEditor;

internal static class SettingsStorage
{
    private const string LAST_APK_PATH_KEY = "com.avmedvedskiy.scrspy.last_apk_path";
    private const string LAST_BUNDLE_NAME_KEY = "com.avmedvedskiy.scrspy.last_bundle_name";
    private const string JIRA_PROJECT_NAME_KEY = "com.avmedvedskiy.scrspy.jira_project_name";
    private const string JIRA_API_KEY = "com.avmedvedskiy.scrspy.jira_api_key";
    private const string JIRA_EMAIL_KEY = "com.avmedvedskiy.scrspy.jira_email";
    private const string JIRA_BASE_URL_KEY = "com.avmedvedskiy.scrspy.jira_base_url";

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
            return;

        EditorPrefs.SetString(LAST_BUNDLE_NAME_KEY, bundleName.Trim());
    }

    public static string GetLastBundleName()
    {
        var bundleName = EditorPrefs.GetString(LAST_BUNDLE_NAME_KEY, string.Empty);
        return string.IsNullOrWhiteSpace(bundleName) ? null : bundleName.Trim();
    }

    public static void SetJiraProjectName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        EditorPrefs.SetString(JIRA_PROJECT_NAME_KEY, value.Trim());
    }

    public static string GetJiraProjectName()
    {
        var value = EditorPrefs.GetString(JIRA_PROJECT_NAME_KEY, string.Empty);
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    public static void SetJiraApiKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        EditorPrefs.SetString(JIRA_API_KEY, value.Trim());
    }

    public static string GetJiraApiKey()
    {
        var value = EditorPrefs.GetString(JIRA_API_KEY, string.Empty);
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    public static void SetJiraEmail(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        EditorPrefs.SetString(JIRA_EMAIL_KEY, value.Trim());
    }

    public static string GetJiraEmail()
    {
        var value = EditorPrefs.GetString(JIRA_EMAIL_KEY, string.Empty);
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    public static void SetJiraBaseUrl(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        EditorPrefs.SetString(JIRA_BASE_URL_KEY, value.Trim());
    }

    public static string GetJiraBaseUrl()
    {
        var value = EditorPrefs.GetString(JIRA_BASE_URL_KEY, string.Empty);
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}

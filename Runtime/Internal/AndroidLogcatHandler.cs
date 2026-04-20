using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

internal static class AndroidLogcatHandler
{
    public static void OpenAndroidLogcatForCurrentApp(string packageName, string deviceId)
    {
        OpenAndroidLogcat(deviceId, packageName);
    }

    public static void OpenAndroidLogcat(string deviceId, string packageName = null)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            Debug.LogWarning("Android Logcat: no connected device found for auto-select.");
            return;
        }

        if (!EditorApplication.ExecuteMenuItem("Window/Analysis/Android Logcat"))
        {
            Debug.LogWarning("Android Logcat window is not available. Is com.unity.mobile.android-logcat installed?");
            return;
        }

        EditorApplication.delayCall += () =>
        {
            try
            {
                var windowType = Type.GetType("Unity.Android.Logcat.AndroidLogcatConsoleWindow, Unity.Mobile.AndroidLogcat.Editor");
                if (windowType == null)
                {
                    windowType = AppDomain.CurrentDomain.GetAssemblies()
                        .Select(assembly => assembly.GetType("Unity.Android.Logcat.AndroidLogcatConsoleWindow"))
                        .FirstOrDefault(type => type != null);
                }

                if (windowType == null)
                {
                    Debug.LogWarning("Android Logcat type not found. Cannot apply auto-select.");
                    return;
                }

                var showMethod = windowType.GetMethod("ShowNewOrExisting", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                var windowInstance = showMethod != null ? showMethod.Invoke(null, null) : EditorWindow.GetWindow(windowType);

                var setAutoSelectMethod = windowType.GetMethod("SetAutoSelect", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (setAutoSelectMethod == null || windowInstance == null)
                {
                    Debug.LogWarning("Android Logcat SetAutoSelect method not found.");
                    return;
                }

                var resolvedPackageName = packageName ?? string.Empty;
                setAutoSelectMethod.Invoke(windowInstance, new object[] { deviceId, resolvedPackageName });
                Debug.Log("Android Logcat auto-select: device=" + deviceId + ", package=" + (string.IsNullOrWhiteSpace(resolvedPackageName) ? "<any>" : resolvedPackageName));
            }
            catch (Exception ex)
            {
                Debug.LogWarning("Android Logcat auto-select failed: " + ex.Message);
            }
        };
    }
}

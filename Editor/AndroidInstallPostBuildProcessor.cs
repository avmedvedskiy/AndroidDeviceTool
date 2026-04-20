using System;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

internal sealed class AndroidInstallPostBuildProcessor : IPostprocessBuildWithReport
{
    public int callbackOrder => 0;

    public void OnPostprocessBuild(BuildReport report)
    {
        if (report == null)
            return;

        if (report.summary.platform != BuildTarget.Android)
            return;

        var outputPath = report.summary.outputPath;
        if (string.IsNullOrWhiteSpace(outputPath))
            return;

        if (!outputPath.EndsWith(".apk", StringComparison.OrdinalIgnoreCase))
            return;

        SettingsStorage.SetLastApkPath(outputPath);
    }
}

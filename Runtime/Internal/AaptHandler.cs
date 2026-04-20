using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

internal static class AaptHandler
{
    private static readonly Regex PackageNameRegex = new Regex("package:\\s+name='([^']+)'", RegexOptions.Compiled);

    public static bool TryGetPackageName(string apkPath, out string packageName, out string error)
    {
        packageName = null;
        error = null;

        if (!File.Exists(apkPath))
        {
            error = "APK not found: " + apkPath;
            return false;
        }

        if (!TryGetAaptPath(out var aaptPath, out error))
            return false;

        var result = RunProcess(aaptPath, "dump badging \"" + apkPath + "\"");
        if (result.ExitCode != 0)
        {
            error =
                "aapt failed with code " + result.ExitCode + ".\n" +
                "aapt: " + aaptPath + "\n" +
                "STDERR: " + SafeText(result.StandardError) + "\n" +
                "STDOUT: " + SafeText(result.StandardOutput);
            return false;
        }

        var match = PackageNameRegex.Match(result.StandardOutput ?? string.Empty);
        if (!match.Success)
        {
            error =
                "Unable to parse package name from aapt output.\n" +
                "aapt: " + aaptPath + "\n" +
                "STDOUT: " + SafeText(result.StandardOutput);
            return false;
        }

        packageName = match.Groups[1].Value;
        if (string.IsNullOrWhiteSpace(packageName))
        {
            error = "aapt returned empty package name for APK: " + apkPath;
            packageName = null;
            return false;
        }

        return true;
    }

    private static bool TryGetAaptPath(out string aaptPath, out string error)
    {
        aaptPath = null;
        error = null;

        if (!TryGetAndroidSdkRootPath(out var sdkRootPath, out error))
            return false;

        var buildToolsDir = Path.Combine(sdkRootPath, "build-tools");
        if (!Directory.Exists(buildToolsDir))
        {
            error = "Android SDK build-tools directory not found: " + buildToolsDir;
            return false;
        }

        var candidate = Directory.GetDirectories(buildToolsDir)
            .OrderByDescending(ParseBuildToolsVersion)
            .SelectMany(dir => new[] { Path.Combine(dir, "aapt.exe"), Path.Combine(dir, "aapt") })
            .FirstOrDefault(File.Exists);

        if (string.IsNullOrEmpty(candidate))
        {
            error = "aapt was not found in Android SDK build-tools: " + buildToolsDir;
            return false;
        }

        aaptPath = candidate;
        return true;
    }

    private static bool TryGetAndroidSdkRootPath(out string sdkRootPath, out string error)
    {
        sdkRootPath = null;
        error = null;

        var type = Type.GetType("UnityEditor.Android.AndroidExternalToolsSettings, UnityEditor.Android.Extensions");
        if (type == null)
        {
            type = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType("UnityEditor.Android.AndroidExternalToolsSettings"))
                .FirstOrDefault(foundType => foundType != null);
        }

        if (type == null)
        {
            error = "Unity Android External Tools API is not available. Ensure Android module is installed.";
            return false;
        }

        var sdkRootProperty = type.GetProperty("sdkRootPath", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        if (sdkRootProperty == null)
        {
            error = "Unity Android External Tools API does not contain sdkRootPath property.";
            return false;
        }

        sdkRootPath = sdkRootProperty.GetValue(null) as string;
        if (string.IsNullOrWhiteSpace(sdkRootPath))
        {
            error = "Android SDK path is empty. Configure it in Unity Preferences > External Tools.";
            return false;
        }

        if (!Directory.Exists(sdkRootPath))
        {
            error = "Android SDK path does not exist: " + sdkRootPath;
            return false;
        }

        return true;
    }

    private static Version ParseBuildToolsVersion(string directoryPath)
    {
        var directoryName = Path.GetFileName(directoryPath) ?? string.Empty;
        var normalized = new string(directoryName.TakeWhile(ch => char.IsDigit(ch) || ch == '.').ToArray());

        return Version.TryParse(normalized, out var version)
            ? version
            : new Version(0, 0, 0, 0);
    }

    private static ProcessResult RunProcess(string fileName, string arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using (var process = Process.Start(startInfo))
        {
            if (process == null)
                throw new InvalidOperationException("Unable to start process: " + fileName);

            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();

            return new ProcessResult(process.ExitCode, stdout, stderr);
        }
    }

    private static string SafeText(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "<empty>" : value.Trim();
    }

    private readonly struct ProcessResult
    {
        public readonly int ExitCode;
        public readonly string StandardOutput;
        public readonly string StandardError;

        public ProcessResult(int exitCode, string standardOutput, string standardError)
        {
            ExitCode = exitCode;
            StandardOutput = standardOutput;
            StandardError = standardError;
        }
    }
}

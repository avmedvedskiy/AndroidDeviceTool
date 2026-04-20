using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEngine;
using Debug = UnityEngine.Debug;

internal sealed class AdbHandler
{
    private readonly string _adbPath;
    private readonly string _workingDirectory;
    private readonly string _deviceId;

    public AdbHandler(string adbPath, string workingDirectory, string deviceId = null)
    {
        _adbPath = adbPath;
        _workingDirectory = workingDirectory;
        _deviceId = deviceId;
    }

    public void StartServer()
    {
        Run("start-server", useDeviceSelector: false);
    }

    public void WaitForDevice()
    {
        Run("wait-for-device");
    }

    public void InstallApkWithFallback(string apkPath, string packageName)
    {
        var primaryArgs = "install -r -d \"" + apkPath + "\"";
        var firstAttempt = Run(primaryArgs, throwOnFailure: false);
        if (firstAttempt.ExitCode == 0)
            return;

        var fallbackArgs = "install -r -d -t \"" + apkPath + "\"";
        Debug.LogWarning("Primary install failed, retrying with -t...");
        var secondAttempt = Run(fallbackArgs, throwOnFailure: false);
        if (secondAttempt.ExitCode == 0)
            return;

        if (IsPackageInstalled(packageName))
        {
            Debug.LogWarning("Package already installed. Uninstalling and reinstalling without prompts...");

            var uninstallResult = Run("uninstall " + packageName, throwOnFailure: false);
            if (uninstallResult.ExitCode == 0)
            {
                var reinstallArgs = "install -d -t \"" + apkPath + "\"";
                var reinstallResult = Run(reinstallArgs, throwOnFailure: false);
                if (reinstallResult.ExitCode == 0)
                    return;

                throw new InvalidOperationException(
                    "Reinstall after uninstall failed.\n" +
                    "Args: " + reinstallArgs + "\n" +
                    "STDERR: " + SafeText(reinstallResult.StandardError) + "\n" +
                    "STDOUT: " + SafeText(reinstallResult.StandardOutput));
            }

            Debug.LogWarning(
                "Automatic uninstall failed for package '" + packageName + "'.\n" +
                "STDERR: " + SafeText(uninstallResult.StandardError) + "\n" +
                "STDOUT: " + SafeText(uninstallResult.StandardOutput));
        }

        var details =
            "Install failed after retry.\n" +
            "First args: " + primaryArgs + "\n" +
            "First stderr: " + SafeText(firstAttempt.StandardError) + "\n" +
            "First stdout: " + SafeText(firstAttempt.StandardOutput) + "\n" +
            "Second args: " + fallbackArgs + "\n" +
            "Second stderr: " + SafeText(secondAttempt.StandardError) + "\n" +
            "Second stdout: " + SafeText(secondAttempt.StandardOutput);

        throw new InvalidOperationException(details);
    }

    public void LaunchInstalledApp(string packageName)
    {
        var launchArgs = "shell monkey -p " + packageName + " -c android.intent.category.LAUNCHER 1";
        var launchResult = Run(launchArgs, throwOnFailure: false);
        if (launchResult.ExitCode == 0)
            return;

        throw new InvalidOperationException(
            "App launch failed for package: " + packageName +
            "\nSTDERR: " + SafeText(launchResult.StandardError) +
            "\nSTDOUT: " + SafeText(launchResult.StandardOutput));
    }

    public string TryGetAndroidVersion()
    {
        return RunSingleLine("shell getprop ro.build.version.release");
    }

    public string TryGetModel()
    {
        return RunSingleLine("shell getprop ro.product.model");
    }

    public string TryGetScreenResolution()
    {
        var result = Run("shell wm size", throwOnFailure: false, logOutput: false);
        if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.StandardOutput))
            return null;

        var lines = result.StandardOutput
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .ToArray();

        var line = lines.FirstOrDefault(value => value.StartsWith("Physical size:", StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(line))
            line = lines.FirstOrDefault(value => value.IndexOf("size:", StringComparison.OrdinalIgnoreCase) >= 0);

        if (string.IsNullOrWhiteSpace(line))
            return null;

        var separatorIndex = line.IndexOf(':');
        if (separatorIndex < 0 || separatorIndex >= line.Length - 1)
            return null;

        var resolution = line.Substring(separatorIndex + 1).Trim();
        return string.IsNullOrWhiteSpace(resolution) ? null : resolution;
    }

    public int? TryGetBatteryLevel()
    {
        var result = Run("shell dumpsys battery", throwOnFailure: false, logOutput: false);
        if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.StandardOutput))
            return null;

        var levelLine = result.StandardOutput
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .FirstOrDefault(line => line.StartsWith("level:", StringComparison.OrdinalIgnoreCase));

        if (string.IsNullOrWhiteSpace(levelLine))
            return null;

        var levelText = levelLine.Substring(levelLine.IndexOf(':') + 1).Trim();
        return int.TryParse(levelText, out var level) ? level : null;
    }

    public bool? TryIsScreenOn()
    {
        var result = Run("shell dumpsys power", throwOnFailure: false, logOutput: false);
        if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.StandardOutput))
            return null;

        var lines = result.StandardOutput
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .ToArray();

        var displayStateLine = lines.FirstOrDefault(line => line.IndexOf("Display Power: state=", StringComparison.OrdinalIgnoreCase) >= 0);
        if (!string.IsNullOrWhiteSpace(displayStateLine))
        {
            if (displayStateLine.IndexOf("state=ON", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            if (displayStateLine.IndexOf("state=OFF", StringComparison.OrdinalIgnoreCase) >= 0)
                return false;
        }

        var wakefulnessLine = lines.FirstOrDefault(line => line.StartsWith("mWakefulness=", StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(wakefulnessLine))
        {
            if (wakefulnessLine.IndexOf("Awake", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            if (wakefulnessLine.IndexOf("Asleep", StringComparison.OrdinalIgnoreCase) >= 0)
                return false;
        }

        var interactiveLine = lines.FirstOrDefault(line => line.StartsWith("mInteractive=", StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(interactiveLine))
        {
            var value = interactiveLine.Substring(interactiveLine.IndexOf('=') + 1).Trim();
            if (bool.TryParse(value, out var interactive))
                return interactive;
        }

        return null;
    }

    public IReadOnlyList<AdbDeviceInfo> GetConnectedDevices()
    {
        var result = Run("devices -l", throwOnFailure: false, useDeviceSelector: false, logOutput: false);
        if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.StandardOutput))
            return Array.Empty<AdbDeviceInfo>();

        var devices = new List<AdbDeviceInfo>();
        var lines = result.StandardOutput
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Skip(1);

        foreach (var line in lines)
        {
            if (TryParseDeviceLine(line, out var device))
                devices.Add(device);
        }

        return devices;
    }

    public string TryGetTargetDeviceId()
    {
        var devices = GetConnectedDevices();
        if (devices.Count == 0)
            return null;

        if (devices.Count > 1)
            Debug.LogWarning("Multiple devices detected. Android Logcat will use first device: " + devices[0].Id);

        return devices[0].Id;
    }

    public void CaptureScreenshot(string outputPath)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
            throw new ArgumentException("Output path is empty.", nameof(outputPath));

        var outputDirectory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(outputDirectory))
            Directory.CreateDirectory(outputDirectory);

        var info = new ProcessStartInfo
        {
            FileName = _adbPath,
            Arguments = BuildArguments("exec-out screencap -p", useDeviceSelector: true),
            WorkingDirectory = _workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using (var process = Process.Start(info))
        {
            if (process == null)
                throw new InvalidOperationException("Unable to start process: " + _adbPath);

            using (var fileStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.Read))
            {
                process.StandardOutput.BaseStream.CopyTo(fileStream);
            }

            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    "Screenshot capture failed with adb exit code " + process.ExitCode +
                    ".\nSTDERR: " + SafeText(stderr));
            }
        }
    }

    public int? TryGetProcessId(string packageName)
    {
        if (string.IsNullOrWhiteSpace(packageName))
            return null;

        var pidofOutput = RunSingleLine("shell pidof " + packageName);
        var pid = TryParseFirstPidToken(pidofOutput);
        if (pid.HasValue)
            return pid;

        var psResult = Run("shell ps -A", throwOnFailure: false, logOutput: false);
        if (psResult.ExitCode != 0 || string.IsNullOrWhiteSpace(psResult.StandardOutput))
            return null;

        var lines = psResult.StandardOutput
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (!trimmed.EndsWith(packageName, StringComparison.Ordinal))
                continue;

            var parsed = TryParseFirstPidToken(trimmed);
            if (parsed.HasValue)
                return parsed;
        }

        return null;
    }

    private bool IsPackageInstalled(string packageName)
    {
        var result = Run("shell pm list packages " + packageName, throwOnFailure: false);

        if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.StandardOutput))
            return false;

        return result.StandardOutput
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Any(line => line.Trim().Equals("package:" + packageName, StringComparison.Ordinal));
    }

    internal void RunCommand(string arguments, bool throwOnFailure = true)
    {
        Run(arguments, throwOnFailure);
    }

    private string RunSingleLine(string arguments)
    {
        var result = Run(arguments, throwOnFailure: false, logOutput: false);
        if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.StandardOutput))
            return null;

        var firstLine = result.StandardOutput
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .FirstOrDefault();

        return string.IsNullOrWhiteSpace(firstLine) ? null : firstLine;
    }

    private ProcessResult Run(string arguments, bool throwOnFailure = true, bool useDeviceSelector = true, bool logOutput = true)
    {
        var fullArguments = BuildArguments(arguments, useDeviceSelector);
        var info = new ProcessStartInfo
        {
            FileName = _adbPath,
            Arguments = fullArguments,
            WorkingDirectory = _workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using (var process = Process.Start(info))
        {
            if (process == null)
                throw new InvalidOperationException("Unable to start process: " + _adbPath);

            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (logOutput && !string.IsNullOrWhiteSpace(stdout))
                Debug.Log(stdout.Trim());
            if (logOutput && !string.IsNullOrWhiteSpace(stderr))
                Debug.LogWarning(stderr.Trim());

            var result = new ProcessResult(process.ExitCode, stdout, stderr);

            if (throwOnFailure && result.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    _adbPath + " exited with code " + result.ExitCode +
                    ". Args: " + fullArguments +
                    "\nSTDERR: " + SafeText(result.StandardError) +
                    "\nSTDOUT: " + SafeText(result.StandardOutput));
            }

            return result;
        }
    }

    private string BuildArguments(string arguments, bool useDeviceSelector)
    {
        if (!useDeviceSelector || string.IsNullOrWhiteSpace(_deviceId))
            return arguments;

        return "-s \"" + _deviceId + "\" " + arguments;
    }

    private static bool TryParseDeviceLine(string line, out AdbDeviceInfo device)
    {
        device = default;

        var parts = line
            .Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length < 2)
            return false;

        var id = parts[0];
        var state = parts[1];

        if (!string.Equals(state, "device", StringComparison.OrdinalIgnoreCase))
            return false;

        string model = null;
        string product = null;
        string deviceName = null;

        foreach (var token in parts.Skip(2))
        {
            var separatorIndex = token.IndexOf(':');
            if (separatorIndex <= 0 || separatorIndex >= token.Length - 1)
                continue;

            var key = token.Substring(0, separatorIndex);
            var value = token.Substring(separatorIndex + 1);

            if (string.Equals(key, "model", StringComparison.Ordinal))
            {
                model = value.Replace('_', ' ');
                continue;
            }

            if (string.Equals(key, "product", StringComparison.Ordinal))
            {
                product = value;
                continue;
            }

            if (string.Equals(key, "device", StringComparison.Ordinal))
                deviceName = value;
        }

        device = new AdbDeviceInfo(id, model, product, deviceName);
        return true;
    }

    private static string SafeText(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "<empty>" : value.Trim();
    }

    private static int? TryParseFirstPidToken(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var tokens = text
            .Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var token in tokens)
        {
            if (int.TryParse(token, out var pid))
                return pid;
        }

        return null;
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

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Debug = UnityEngine.Debug;

internal static class CodexCliHandler
{
    public static async Task<string> DescribeTextAsync(string title, string description)
    {
        var prompt = BuildPrompt(title, description);
        return await ExecuteCodexAsync(prompt, useStdin: false);
    }

    public static async Task<string> AnalyzeLogsAsync(string sessionDirectory, string currentDescription)
    {
        if (string.IsNullOrWhiteSpace(sessionDirectory) || !Directory.Exists(sessionDirectory))
            throw new InvalidOperationException("Session directory is not found: " + sessionDirectory);

        var logPath = Path.Combine(sessionDirectory, "log.txt");
        if (!File.Exists(logPath))
        {
            var unityLogPath = Path.Combine(sessionDirectory, "log_unity.txt");
            if (File.Exists(unityLogPath))
                logPath = unityLogPath;
        }

        if (!File.Exists(logPath))
            throw new InvalidOperationException("No log file found in session folder.");

        var reversedLogTail = ReadRecentLogTailReversed(logPath, 1200);
        if (string.IsNullOrWhiteSpace(reversedLogTail))
            throw new InvalidOperationException("Log file is empty: " + logPath);

        var prompt = BuildLogAnalysisPrompt(currentDescription, reversedLogTail);
        return await ExecuteCodexAsync(prompt, useStdin: true);
    }

    private static async Task<string> ExecuteCodexAsync(string prompt, bool useStdin)
    {
        var escapedPrompt = EscapeCommandArgument(prompt);
        var outputFilePath = Path.Combine(Path.GetTempPath(), "codex_bug_description_" + Guid.NewGuid().ToString("N") + ".txt");
        var promptFilePath = useStdin
            ? Path.Combine(Path.GetTempPath(), "codex_prompt_" + Guid.NewGuid().ToString("N") + ".txt")
            : null;
        var projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
        var codexExecutablePath = ResolveCodexExecutablePath();
        try
        {
            if (string.IsNullOrWhiteSpace(codexExecutablePath))
            {
                throw new InvalidOperationException(
                    "Codex CLI executable was not found. Install @openai/codex and ensure codex.cmd is accessible.\n" +
                    "Expected locations include %USERPROFILE%\\AppData\\Roaming\\npm\\codex.cmd.");
            }

            var arguments = useStdin
                ? "exec - --output-last-message \"" + outputFilePath + "\" --skip-git-repo-check --sandbox read-only"
                : "exec --output-last-message \"" + outputFilePath + "\" --skip-git-repo-check --sandbox read-only \"" + escapedPrompt + "\"";
            var fileName = codexExecutablePath;
            var extension = Path.GetExtension(codexExecutablePath);
            var usePromptFileRedirection = false;

            if (useStdin && !string.IsNullOrWhiteSpace(promptFilePath))
            {
                var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
                await File.WriteAllTextAsync(promptFilePath, prompt ?? string.Empty, utf8);
            }

            if (string.Equals(extension, ".cmd", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(extension, ".bat", StringComparison.OrdinalIgnoreCase))
            {
                fileName = "cmd.exe";
                if (useStdin && !string.IsNullOrWhiteSpace(promptFilePath))
                {
                    arguments = "/d /s /c \"\"" + codexExecutablePath + "\" " + arguments + " < \"" + promptFilePath + "\"\"";
                    usePromptFileRedirection = true;
                }
                else
                {
                    arguments = "/d /s /c \"\"" + codexExecutablePath + "\" " + arguments + "\"";
                }
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = projectRoot,
                UseShellExecute = false,
                RedirectStandardInput = useStdin && !usePromptFileRedirection,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            Debug.Log("Codex CLI start: " + startInfo.FileName + " " + startInfo.Arguments);

            using var process = Process.Start(startInfo);
            if (process == null)
                throw new InvalidOperationException("Failed to start codex CLI process.");

            if (useStdin && !usePromptFileRedirection)
            {
                await WriteUtf8ToStandardInputAsync(process, prompt ?? string.Empty);
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            await Task.Run(process.WaitForExit);

            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    "Codex CLI exited with code " + process.ExitCode +
                    "\nSTDERR: " + (string.IsNullOrWhiteSpace(stderr) ? "<empty>" : stderr.Trim()) +
                    "\nSTDOUT: " + (string.IsNullOrWhiteSpace(stdout) ? "<empty>" : stdout.Trim()));
            }

            if (File.Exists(outputFilePath))
            {
                var result = (await File.ReadAllTextAsync(outputFilePath)).Trim();
                if (!string.IsNullOrWhiteSpace(result))
                    return result;
            }

            var fallback = (stdout ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(fallback))
                return fallback;

            throw new InvalidOperationException("Codex CLI finished successfully but returned empty output.");
        }
        finally
        {
            try
            {
                if (File.Exists(outputFilePath))
                    File.Delete(outputFilePath);
                if (!string.IsNullOrWhiteSpace(promptFilePath) && File.Exists(promptFilePath))
                    File.Delete(promptFilePath);
            }
            catch
            {
                // ignore cleanup failures
            }
        }
    }

    private static string ResolveCodexExecutablePath()
    {
        var candidates = new[]
        {
            "codex",
            "codex.cmd",
            "codex.exe"
        };

        foreach (var candidate in candidates)
        {
            if (TryRunWhere(candidate, out var resolved))
                return resolved;
        }

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(userProfile))
        {
            var roamingNpm = Path.Combine(userProfile, "AppData", "Roaming", "npm", "codex.cmd");
            if (File.Exists(roamingNpm))
                return roamingNpm;

            var localNpm = Path.Combine(userProfile, "AppData", "Local", "npm", "codex.cmd");
            if (File.Exists(localNpm))
                return localNpm;
        }

        return null;
    }

    private static bool TryRunWhere(string commandName, out string resolvedPath)
    {
        resolvedPath = null;

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "where",
                Arguments = commandName,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null)
                return false;

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
                return false;

            var path = output
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .OrderByDescending(line =>
                {
                    var ext = Path.GetExtension(line);
                    if (string.Equals(ext, ".exe", StringComparison.OrdinalIgnoreCase))
                        return 3;
                    if (string.Equals(ext, ".cmd", StringComparison.OrdinalIgnoreCase))
                        return 2;
                    if (string.Equals(ext, ".bat", StringComparison.OrdinalIgnoreCase))
                        return 1;
                    return 0;
                })
                .FirstOrDefault();

            if (string.IsNullOrWhiteSpace(path))
                return false;

            resolvedPath = path.Trim();
            if (!Path.HasExtension(resolvedPath))
            {
                var cmdPath = resolvedPath + ".cmd";
                if (File.Exists(cmdPath))
                    resolvedPath = cmdPath;
            }

            return File.Exists(resolvedPath);
        }
        catch
        {
            return false;
        }
    }

    private static string BuildPrompt(string title, string description)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Rewrite the bug description to be clear and concise for Jira.");
        builder.AppendLine("Return plain text only, no markdown, no code blocks.");
        builder.AppendLine("If possible, keep structure: Steps to Reproduce, Actual Result, Expected Result.");
        builder.AppendLine();
        builder.AppendLine("Bug title:");
        builder.AppendLine(string.IsNullOrWhiteSpace(title) ? "<empty>" : title.Trim());
        builder.AppendLine();
        builder.AppendLine("Current description:");
        builder.AppendLine(string.IsNullOrWhiteSpace(description) ? "<empty>" : description.Trim());
        return builder.ToString();
    }

    private static string BuildLogAnalysisPrompt(string currentDescription, string reversedLogTail)
    {
        var builder = new StringBuilder();
        builder.AppendLine("$galaxy-unity");
        builder.AppendLine("Нужно найти подозрительные ошибки в логах, которые привели к багу.");
        builder.AppendLine("Логи уже отсортированы с последнего события вначале.");
        builder.AppendLine("Приоритет анализа: сначала Exception/Crash/Fatal и связанные stack trace, потом остальные подозрительные ошибки.");
        builder.AppendLine("Не перечисляй все логи подряд, фокусируйся на вероятной причине бага.");
        builder.AppendLine("Сопоставь найденные ошибки с тем, что уже написано в описании бага.");
        builder.AppendLine("Ответ верни на русском, коротко и по делу.");
        builder.AppendLine("Формат ответа:");
        builder.AppendLine("1) Главная подозрительная ошибка");
        builder.AppendLine("2) Что проверить в Unity/коде и насколько это совпадает с Description");
        builder.AppendLine();
        builder.AppendLine("Текущий текст описания бага:");
        builder.AppendLine(string.IsNullOrWhiteSpace(currentDescription) ? "<empty>" : currentDescription.Trim());
        builder.AppendLine();
        builder.AppendLine("Логи (последние события вконце):");
        builder.AppendLine(reversedLogTail);
        return builder.ToString();
    }

    private static string ReadRecentLogTailReversed(string logPath, int maxLines)
    {
        var buffer = new Queue<string>(Math.Max(1, maxLines));
        foreach (var line in File.ReadLines(logPath))
        {
            if (buffer.Count >= maxLines)
                buffer.Dequeue();
            buffer.Enqueue(line ?? string.Empty);
        }

        if (buffer.Count == 0)
            return string.Empty;

        var lines = buffer.ToArray();
        Array.Reverse(lines);
        return string.Join(Environment.NewLine, lines);
    }

    private static string EscapeCommandArgument(string value)
    {
        return (value ?? string.Empty)
            .Replace("\r", " ")
            .Replace("\n", " ")
            .Replace("\"", "\\\"");
    }

    private static async Task WriteUtf8ToStandardInputAsync(Process process, string value)
    {
        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        var bytes = utf8.GetBytes(value ?? string.Empty);
        await process.StandardInput.BaseStream.WriteAsync(bytes, 0, bytes.Length);
        await process.StandardInput.BaseStream.FlushAsync();
        process.StandardInput.Close();
    }
}

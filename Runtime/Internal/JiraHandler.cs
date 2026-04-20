using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEngine;

internal static class JiraHandler
{
    private static readonly Regex IssueKeyRegex = new Regex("\"key\"\\s*:\\s*\"([^\"]+)\"", RegexOptions.Compiled);

    public static async Task<string> CreateIssueWithAttachmentsAsync(
        string jiraBaseUrl,
        string email,
        string apiKey,
        string projectKey,
        string title,
        string description,
        IReadOnlyList<string> attachmentPaths)
    {
        var normalizedBaseUrl = NormalizeBaseUrl(jiraBaseUrl);
        using (var client = CreateHttpClient(normalizedBaseUrl, email, apiKey))
        {
            var issueKey = await CreateIssueInternalAsync(client, projectKey, title, description);
            await UploadAttachmentsAsync(client, issueKey, attachmentPaths);
            return issueKey;
        }
    }

    public static string GetIssueBrowseUrl(string jiraBaseUrl, string issueKey)
    {
        var normalizedBaseUrl = NormalizeBaseUrl(jiraBaseUrl);
        return normalizedBaseUrl + "/browse/" + issueKey;
    }

    public static async Task<string> CreateIssueAsync(
        string jiraBaseUrl,
        string email,
        string apiKey,
        string projectKey,
        string title,
        string description)
    {
        var normalizedBaseUrl = NormalizeBaseUrl(jiraBaseUrl);
        using (var client = CreateHttpClient(normalizedBaseUrl, email, apiKey))
        {
            return await CreateIssueInternalAsync(client, projectKey, title, description);
        }
    }

    public static async Task UploadAttachmentsAsync(
        string jiraBaseUrl,
        string email,
        string apiKey,
        string issueKey,
        IReadOnlyList<string> attachmentPaths,
        Action<int, int, string> onUploaded = null)
    {
        var normalizedBaseUrl = NormalizeBaseUrl(jiraBaseUrl);
        using var client = CreateHttpClient(normalizedBaseUrl, email, apiKey);
        await UploadAttachmentsInternalAsync(client, issueKey, attachmentPaths, onUploaded);
    }

    public static IReadOnlyList<string> CollectSessionAttachmentPaths(string sessionDirectory)
    {
        if (string.IsNullOrWhiteSpace(sessionDirectory) || !Directory.Exists(sessionDirectory))
            return Array.Empty<string>();

        var allowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".png", ".jpg", ".jpeg",
            ".txt", ".log",
            ".mkv", ".mp4", ".webm"
        };

        return Directory.GetFiles(sessionDirectory, "*", SearchOption.TopDirectoryOnly)
            .Where(path =>
            {
                var fileName = Path.GetFileName(path);
                if (fileName.Equals("summary.txt", StringComparison.OrdinalIgnoreCase))
                    return false;

                var extension = Path.GetExtension(path);
                return allowedExtensions.Contains(extension);
            })
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static HttpClient CreateHttpClient(string jiraBaseUrl, string email, string apiKey)
    {
        var authBytes = Encoding.UTF8.GetBytes(email + ":" + apiKey);
        var authToken = Convert.ToBase64String(authBytes);

        var client = new HttpClient
        {
            BaseAddress = new Uri(jiraBaseUrl, UriKind.Absolute),
            Timeout = TimeSpan.FromSeconds(60)
        };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authToken);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    private static async Task<string> CreateIssueInternalAsync(HttpClient client, string projectKey, string title,
        string description)
    {
        var payload = BuildCreateIssuePayload(projectKey, title, description);
        using (var request = new HttpRequestMessage(HttpMethod.Post, "/rest/api/3/issue"))
        {
            request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
            using (var response = await client.SendAsync(request))
            {
                var body = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                    throw new InvalidOperationException("Jira issue creation failed (" + (int)response.StatusCode +
                                                        "): " + body);

                var match = IssueKeyRegex.Match(body ?? string.Empty);
                if (!match.Success)
                    throw new InvalidOperationException("Jira issue key was not found in response: " + body);

                return match.Groups[1].Value;
            }
        }
    }

    private static async Task UploadAttachmentsAsync(HttpClient client, string issueKey,
        IReadOnlyList<string> attachmentPaths)
    {
        await UploadAttachmentsInternalAsync(client, issueKey, attachmentPaths, null);
    }

    private static async Task UploadAttachmentsInternalAsync(HttpClient client, string issueKey,
        IReadOnlyList<string> attachmentPaths, Action<int, int, string> onUploaded)
    {
        if (client == null)
            throw new ArgumentNullException(nameof(client));

        if (string.IsNullOrWhiteSpace(issueKey))
            throw new InvalidOperationException("Issue key is empty for Jira attachments upload.");

        if (attachmentPaths == null)
            return;

        var safePaths = attachmentPaths
            .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
            .ToArray();

        if (safePaths.Length == 0)
            return;

        var total = safePaths.Length;
        var uploaded = 0;

        foreach (var attachmentPath in safePaths)
        {
            var fileName = Path.GetFileName(attachmentPath);
            if (string.IsNullOrWhiteSpace(fileName))
                fileName = "attachment.bin";

            var multipart = new MultipartFormDataContent();
            var bytes = await File.ReadAllBytesAsync(attachmentPath);
            var content = new ByteArrayContent(bytes);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            multipart.Add(content, "file", fileName);

            using var request =
                new HttpRequestMessage(HttpMethod.Post, "/rest/api/3/issue/" + issueKey + "/attachments");
            request.Headers.TryAddWithoutValidation("X-Atlassian-Token", "no-check");
            request.Content = multipart;

            using var response = await client.SendAsync(request);

            var body = response.Content != null
                ? await response.Content.ReadAsStringAsync()
                : string.Empty;

            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(
                    "Jira attachment upload failed for '" + fileName +
                    "' (" + (int)response.StatusCode + "): " + body);
            }

            uploaded++;
            onUploaded?.Invoke(uploaded, total, fileName);
        }
    }

    private static string BuildCreateIssuePayload(string projectKey, string title, string description)
    {
        var safeProjectKey = JsonEscape(projectKey);
        var safeTitle = JsonEscape(title);
        var safeDescription = JsonEscape(description);

        return "{"
               + "\"fields\":{"
               + "\"project\":{\"key\":\"" + safeProjectKey + "\"},"
               + "\"summary\":\"" + safeTitle + "\","
               + "\"description\":{"
               + "\"type\":\"doc\","
               + "\"version\":1,"
               + "\"content\":[{\"type\":\"paragraph\",\"content\":[{\"type\":\"text\",\"text\":\"" + safeDescription +
               "\"}]}]"
               + "},"
               + "\"issuetype\":{\"name\":\"Bug\"}"
               + "}"
               + "}";
    }

    private static string NormalizeBaseUrl(string jiraBaseUrl)
    {
        var value = (jiraBaseUrl ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException("Jira Base URL is empty.");

        if (!value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            value = "https://" + value;
        }

        return value.TrimEnd('/');
    }

    private static string JsonEscape(string value)
    {
        return (value ?? string.Empty)
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\r", "\\r")
            .Replace("\n", "\\n")
            .Replace("\t", "\\t");
    }
}

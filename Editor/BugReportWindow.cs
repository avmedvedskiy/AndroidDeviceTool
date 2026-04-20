using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

internal sealed class BugReportWindow : EditorWindow
{
    private string _screenshotPath;
    private string _defaultScreenshotDirectory;
    private string _sessionDirectory;
    private Texture2D _previewTexture;

    private TextField _jiraBaseUrlField;
    private TextField _jiraProjectNameField;
    private TextField _jiraEmailField;
    private TextField _jiraApiKeyField;
    private TextField _userIdField;
    private TextField _environmentField;
    private TextField _titleField;
    private TextField _descriptionField;
    private Image _previewImage;

    public static void Open(string screenshotPath, string defaultScreenshotDirectory = null)
    {
        var window = GetWindow<BugReportWindow>("Bug report");
        window.minSize = new Vector2(760f, 430f);
        window.Initialize(screenshotPath, defaultScreenshotDirectory);
        window.Show();
    }

    private void Initialize(string screenshotPath, string defaultScreenshotDirectory)
    {
        _screenshotPath = screenshotPath;
        _defaultScreenshotDirectory = defaultScreenshotDirectory;
        _sessionDirectory = !string.IsNullOrWhiteSpace(defaultScreenshotDirectory)
            ? defaultScreenshotDirectory
            : Path.GetDirectoryName(screenshotPath);
        LoadPreviewTexture();
        BuildLayout();
    }

    private void BuildLayout()
    {
        var root = rootVisualElement;
        root.Clear();
        root.style.flexDirection = FlexDirection.Row;
        root.style.paddingLeft = 12f;
        root.style.paddingRight = 12f;
        root.style.paddingTop = 12f;
        root.style.paddingBottom = 12f;

        var leftPanel = new VisualElement();
        leftPanel.style.flexGrow = 1f;
        leftPanel.style.marginRight = 12f;
        leftPanel.style.flexDirection = FlexDirection.Column;
        root.Add(leftPanel);

        var jiraHeader = new Label("Jira Settings");
        jiraHeader.style.unityFontStyleAndWeight = FontStyle.Bold;
        jiraHeader.style.marginBottom = 6f;
        leftPanel.Add(jiraHeader);

        var jiraBaseUrlLabel = new Label("Jira Base URL");
        jiraBaseUrlLabel.style.marginBottom = 2f;
        leftPanel.Add(jiraBaseUrlLabel);

        _jiraBaseUrlField = new TextField();
        _jiraBaseUrlField.style.marginBottom = 8f;
        _jiraBaseUrlField.SetValueWithoutNotify(SettingsStorage.GetJiraBaseUrl() ?? string.Empty);
        leftPanel.Add(_jiraBaseUrlField);

        var jiraProjectLabel = new Label("Jira Project Name");
        jiraProjectLabel.style.marginBottom = 2f;
        leftPanel.Add(jiraProjectLabel);

        _jiraProjectNameField = new TextField();
        _jiraProjectNameField.style.marginBottom = 8f;
        _jiraProjectNameField.SetValueWithoutNotify(SettingsStorage.GetJiraProjectName() ?? string.Empty);
        leftPanel.Add(_jiraProjectNameField);

        var jiraEmailLabel = new Label("Email");
        jiraEmailLabel.style.marginBottom = 2f;
        leftPanel.Add(jiraEmailLabel);

        _jiraEmailField = new TextField();
        _jiraEmailField.style.marginBottom = 8f;
        _jiraEmailField.SetValueWithoutNotify(SettingsStorage.GetJiraEmail() ?? string.Empty);
        leftPanel.Add(_jiraEmailField);

        var jiraApiLabel = new Label("Jira API key");
        jiraApiLabel.style.marginBottom = 2f;
        leftPanel.Add(jiraApiLabel);

        _jiraApiKeyField = new TextField { isPasswordField = true };
        _jiraApiKeyField.style.marginBottom = 10f;
        _jiraApiKeyField.SetValueWithoutNotify(SettingsStorage.GetJiraApiKey() ?? string.Empty);
        leftPanel.Add(_jiraApiKeyField);

        var userIdLabel = new Label("User ID");
        userIdLabel.style.marginBottom = 2f;
        leftPanel.Add(userIdLabel);

        _userIdField = new TextField();
        _userIdField.style.marginBottom = 8f;
        leftPanel.Add(_userIdField);

        var environmentLabel = new Label("Environment");
        environmentLabel.style.marginBottom = 2f;
        leftPanel.Add(environmentLabel);

        _environmentField = new TextField();
        _environmentField.style.marginBottom = 10f;
        leftPanel.Add(_environmentField);

        var titleLabel = new Label("Title");
        titleLabel.style.marginBottom = 2f;
        leftPanel.Add(titleLabel);

        _titleField = new TextField();
        _titleField.style.marginBottom = 8f;
        leftPanel.Add(_titleField);

        var descriptionLabel = new Label("Description");
        descriptionLabel.style.marginBottom = 2f;
        leftPanel.Add(descriptionLabel);

        _descriptionField = new TextField { multiline = true };
        _descriptionField.style.flexGrow = 1f;
        _descriptionField.style.minHeight = 140f;
        _descriptionField.style.marginBottom = 10f;
        leftPanel.Add(_descriptionField);

        var actionsRow = new VisualElement();
        actionsRow.style.flexDirection = FlexDirection.Row;
        actionsRow.style.alignItems = Align.Center;
        leftPanel.Add(actionsRow);

        var makeAiButton = new Button(OnMakeAiClicked) { text = "Make AI" };
        makeAiButton.style.height = 30f;
        makeAiButton.style.width = 130f;
        makeAiButton.style.marginRight = 8f;
        actionsRow.Add(makeAiButton);

        var submitButton = new Button(OnSubmitClicked) { text = "Submit" };
        submitButton.style.height = 30f;
        submitButton.style.width = 110f;
        submitButton.style.marginRight = 8f;
        submitButton.style.backgroundColor = new Color(0.17f, 0.62f, 0.24f);
        submitButton.style.color = Color.white;
        actionsRow.Add(submitButton);

        var cancelButton = new Button(Close) { text = "Cancel" };
        cancelButton.style.height = 30f;
        cancelButton.style.width = 110f;
        cancelButton.style.backgroundColor = new Color(0.72f, 0.2f, 0.2f);
        cancelButton.style.color = Color.white;
        actionsRow.Add(cancelButton);

        var rightPanel = new VisualElement();
        rightPanel.style.width = 300f;
        rightPanel.style.flexDirection = FlexDirection.Column;
        root.Add(rightPanel);

        var previewHeader = new Label("Screenshot");
        previewHeader.style.unityFontStyleAndWeight = FontStyle.Bold;
        previewHeader.style.marginBottom = 6f;
        rightPanel.Add(previewHeader);

        var changeScreenshotButton = new Button(OnChangeScreenshotClicked) { text = "Change screenshot" };
        changeScreenshotButton.style.height = 24f;
        changeScreenshotButton.style.marginBottom = 6f;
        rightPanel.Add(changeScreenshotButton);

        var imageContainer = new VisualElement();
        imageContainer.style.flexGrow = 1f;
        imageContainer.style.borderTopWidth = 1f;
        imageContainer.style.borderBottomWidth = 1f;
        imageContainer.style.borderLeftWidth = 1f;
        imageContainer.style.borderRightWidth = 1f;
        imageContainer.style.borderTopColor = new Color(0.22f, 0.22f, 0.22f);
        imageContainer.style.borderBottomColor = new Color(0.22f, 0.22f, 0.22f);
        imageContainer.style.borderLeftColor = new Color(0.22f, 0.22f, 0.22f);
        imageContainer.style.borderRightColor = new Color(0.22f, 0.22f, 0.22f);
        imageContainer.style.justifyContent = Justify.Center;
        imageContainer.style.alignItems = Align.Center;
        imageContainer.style.paddingLeft = 6f;
        imageContainer.style.paddingRight = 6f;
        imageContainer.style.paddingTop = 6f;
        imageContainer.style.paddingBottom = 6f;
        rightPanel.Add(imageContainer);

        _previewImage = new Image();
        _previewImage.style.width = Length.Percent(100f);
        _previewImage.style.height = Length.Percent(100f);
        _previewImage.scaleMode = ScaleMode.ScaleToFit;
        _previewImage.image = _previewTexture;
        imageContainer.Add(_previewImage);
    }

    private void LoadPreviewTexture()
    {
        _previewTexture = null;

        if (string.IsNullOrWhiteSpace(_screenshotPath) || !File.Exists(_screenshotPath))
            return;

        var bytes = File.ReadAllBytes(_screenshotPath);
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!texture.LoadImage(bytes))
        {
            DestroyImmediate(texture);
            return;
        }

        texture.hideFlags = HideFlags.HideAndDontSave;
        _previewTexture = texture;
    }

    private async void OnSubmitClicked()
    {
        var jiraBaseUrl = (_jiraBaseUrlField.value ?? string.Empty).Trim();
        var jiraProjectName = (_jiraProjectNameField.value ?? string.Empty).Trim();
        var jiraEmail = (_jiraEmailField.value ?? string.Empty).Trim();
        var jiraApiKey = (_jiraApiKeyField.value ?? string.Empty).Trim();
        var userId = (_userIdField.value ?? string.Empty).Trim();
        var environment = (_environmentField.value ?? string.Empty).Trim();
        var title = (_titleField.value ?? string.Empty).Trim();
        var description = (_descriptionField.value ?? string.Empty).Trim();
        var finalDescription = BuildFinalDescription(description, userId, environment, _sessionDirectory);

        if (string.IsNullOrWhiteSpace(jiraBaseUrl) ||
            string.IsNullOrWhiteSpace(jiraProjectName) ||
            string.IsNullOrWhiteSpace(jiraEmail) ||
            string.IsNullOrWhiteSpace(jiraApiKey) ||
            string.IsNullOrWhiteSpace(title))
        {
            EditorUtility.DisplayDialog("Bug report", "Fill required fields: Jira Base URL, Project Name, Email, API key, Title.", "OK");
            return;
        }

        if (string.IsNullOrWhiteSpace(_sessionDirectory) || !Directory.Exists(_sessionDirectory))
        {
            EditorUtility.DisplayDialog("Bug report", "Session folder is not available. Select a valid session screenshot/folder first.", "OK");
            return;
        }

        SettingsStorage.SetJiraBaseUrl(jiraBaseUrl);
        SettingsStorage.SetJiraProjectName(jiraProjectName);
        SettingsStorage.SetJiraEmail(jiraEmail);
        SettingsStorage.SetJiraApiKey(jiraApiKey);

        var attachments = JiraHandler.CollectSessionAttachmentPaths(_sessionDirectory);
        if (!attachments.Any())
        {
            EditorUtility.DisplayDialog("Bug report", "No attachments found in session folder.", "OK");
            return;
        }
        
        EditorUtility.DisplayProgressBar("Bug report", "Creating Jira issue...", 0.2f);
        //try
        //{
            Debug.Log("Jira: creating issue in project '" + jiraProjectName + "'...");
            var issueKey = await JiraHandler.CreateIssueAsync(
                jiraBaseUrl,
                jiraEmail,
                jiraApiKey,
                jiraProjectName,
                title,
                finalDescription);

            Debug.Log("Jira: issue created: " + issueKey);
            EditorUtility.DisplayProgressBar("Bug report", "Issue created: " + issueKey + ". Uploading attachments...", 0.45f);

            if (attachments.Any())
            {
                await JiraHandler.UploadAttachmentsAsync(
                    jiraBaseUrl,
                    jiraEmail,
                    jiraApiKey,
                    issueKey,
                    attachments,
                    (uploaded, total, fileName) =>
                    {
                        var progress = total <= 0 ? 0.95f : 0.45f + 0.5f * ((float)uploaded / total);
                        EditorUtility.DisplayProgressBar("Bug report", "Uploading " + uploaded + "/" + total + ": " + fileName, progress);
                        Debug.Log("Jira: uploaded attachment " + uploaded + "/" + total + ": " + fileName);

                    });
            }

            var issueUrl = JiraHandler.GetIssueBrowseUrl(jiraBaseUrl, issueKey);
            Debug.Log("Bug report submitted to Jira: " + issueKey + ". Attachments: " + attachments.Count + ". URL: " + issueUrl);
            EditorUtility.DisplayDialog("Bug report", "Issue created: " + issueKey, "OK");
            Application.OpenURL(issueUrl);
            Close();
        //}
        /*
        catch (Exception ex)
        {
            Debug.LogError("Jira submission failed: " + ex.Message);
            EditorUtility.DisplayDialog("Bug report", "Jira submission failed:\n" + ex.Message, "OK");
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
        */
    }

    private async void OnMakeAiClicked()
    {
        if (string.IsNullOrWhiteSpace(_sessionDirectory) || !Directory.Exists(_sessionDirectory))
        {
            EditorUtility.DisplayDialog("Bug report", "Session folder is not available for log analysis.", "OK");
            return;
        }

        EditorUtility.DisplayProgressBar("Bug report", "Analyzing logs with Codex CLI...", 0.3f);
        try
        {
            var currentDescription = _descriptionField.value ?? string.Empty;
            var analysis = await CodexCliHandler.AnalyzeLogsAsync(_sessionDirectory, currentDescription);

            var potentialIssueBlock = "Potential issue: " + (analysis ?? string.Empty).Trim();
            var mergedDescription = string.IsNullOrWhiteSpace(currentDescription)
                ? potentialIssueBlock
                : currentDescription.TrimEnd() + Environment.NewLine + potentialIssueBlock;

            _descriptionField.SetValueWithoutNotify(mergedDescription);
            Debug.Log("Codex CLI: Make AI completed.");
        }
        catch (Exception ex)
        {
            Debug.LogError("Codex CLI Make AI failed: " + ex.Message);
            EditorUtility.DisplayDialog("Bug report", "Codex CLI Make AI failed:\n" + ex.Message, "OK");
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    private void OnChangeScreenshotClicked()
    {
        var initialDirectory = string.Empty;
        if (!string.IsNullOrWhiteSpace(_screenshotPath))
        {
            initialDirectory = Path.GetDirectoryName(_screenshotPath) ?? string.Empty;
        }
        else if (!string.IsNullOrWhiteSpace(_defaultScreenshotDirectory))
        {
            initialDirectory = _defaultScreenshotDirectory;
        }

        var selectedPath = EditorUtility.OpenFilePanelWithFilters(
            "Select screenshot",
            initialDirectory,
            new[] { "Image files", "png,jpg,jpeg", "All files", "*" });

        if (string.IsNullOrWhiteSpace(selectedPath))
            return;

        _screenshotPath = selectedPath;
        _sessionDirectory = Path.GetDirectoryName(selectedPath);
        LoadPreviewTexture();
        if (_previewImage != null)
            _previewImage.image = _previewTexture;
    }

    private void OnDisable()
    {
        if (_previewTexture != null)
        {
            DestroyImmediate(_previewTexture);
            _previewTexture = null;
        }
    }

    private static string BuildFinalDescription(string description, string userId, string environment, string sessionDirectory)
    {
        var device = TryGetSummaryValue(sessionDirectory, "Device");
        var androidVersion = TryGetSummaryValue(sessionDirectory, "Android");
        var screenResolution = TryGetSummaryValue(sessionDirectory, "Screen resolution");
        var build = TryGetSummaryValue(sessionDirectory, "Build");
        if (string.IsNullOrWhiteSpace(build))
            build = TryGetSummaryValue(sessionDirectory, "Bundle Name");

        var builder = new StringBuilder();
        builder.AppendLine("Device: " + CoalesceOrUnknown(device));
        builder.AppendLine("Android version: " + CoalesceOrUnknown(androidVersion));
        builder.AppendLine("Screen resolution: " + CoalesceOrUnknown(screenResolution));
        builder.AppendLine("Build: " + CoalesceOrUnknown(build));
        builder.AppendLine("User id: " + CoalesceOrUnknown(userId));
        builder.AppendLine("Enviroment: " + CoalesceOrUnknown(environment));
        builder.AppendLine();
        builder.AppendLine("---");
        builder.AppendLine();
        builder.AppendLine("Bug description: " + CoalesceOrUnknown(description));
        return builder.ToString().Trim();
    }

    private static string TryGetSummaryValue(string sessionDirectory, string key)
    {
        if (string.IsNullOrWhiteSpace(sessionDirectory))
            return null;

        var summaryPath = Path.Combine(sessionDirectory, "summary.txt");
        if (!File.Exists(summaryPath))
            return null;

        var prefix = key + ":";
        foreach (var line in File.ReadLines(summaryPath))
        {
            if (!line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;

            var value = line.Substring(prefix.Length).Trim();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        return null;
    }

    private static string CoalesceOrUnknown(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "Unknown" : value.Trim();
    }
}

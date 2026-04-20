using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Debug = UnityEngine.Debug;

internal sealed class AndroidInstallWindow : EditorWindow
{
    private const string MenuPath = "Tools/BuildTools/Android/Open Devices Window";
    private const float DeviceCardWidth = 225f;
    private const float DeviceCardHeight = 190f;
    private const float MinWindowWidth = 520f;
    private const float MinWindowHeight = 395f;
    private const float DeviceViewportMinHeight = 208f;

    private readonly List<DeviceSelection> _deviceSelections = new List<DeviceSelection>();
    private readonly Dictionary<string, SessionRuntimeState> _sessionsByDeviceId = new Dictionary<string, SessionRuntimeState>();
    private readonly Dictionary<string, string> _selectedSessionDirectoryByDeviceId = new Dictionary<string, string>();

    private TextField _apkPathField;
    private TextField _bundleNameField;
    private ScrollView _devicesScrollView;
    private VisualElement _deviceCardsContainer;
    private Button _installButton;
    private Label _statusLabel;

    private string _selectedApkPath;
    private string _bundleName;

    [MenuItem(MenuPath)]
    private static void Open()
    {
        var window = GetWindow<AndroidInstallWindow>("Android Install");
        var minSize = new Vector2(MinWindowWidth, MinWindowHeight);
        window.minSize = minSize;

        var position = window.position;
        if (position.width < minSize.x || position.height < minSize.y)
        {
            position.width = minSize.x;
            position.height = minSize.y;
            window.position = position;
        }
    }

    public void CreateGUI()
    {
        BuildLayout();
        RestoreLastApkPath();
        RestoreBundleName();
        RefreshDeviceList();
    }

    private void OnDisable()
    {
        StopAllSessions();
    }

    private void BuildLayout()
    {
        var root = rootVisualElement;
        root.Clear();
        root.style.flexDirection = FlexDirection.Column;
        root.style.paddingLeft = 10f;
        root.style.paddingRight = 10f;
        root.style.paddingTop = 10f;
        root.style.paddingBottom = 10f;

        var apkLabel = new Label("APK");
        apkLabel.style.marginBottom = 4f;
        root.Add(apkLabel);

        var apkRow = new VisualElement();
        apkRow.style.flexDirection = FlexDirection.Row;
        apkRow.style.alignItems = Align.Center;
        apkRow.style.marginBottom = 8f;

        _apkPathField = new TextField();
        _apkPathField.isReadOnly = true;
        _apkPathField.style.flexGrow = 1f;
        _apkPathField.style.flexShrink = 1f;
        _apkPathField.style.minWidth = 0f;
        _apkPathField.style.marginRight = 8f;
        apkRow.Add(_apkPathField);

        var chooseApkButton = new Button(OnSelectApkClicked) { text = "Choose APK" };
        chooseApkButton.style.width = 110f;
        chooseApkButton.style.flexShrink = 0f;
        chooseApkButton.style.marginRight = 8f;
        apkRow.Add(chooseApkButton);

        _installButton = new Button(InstallSelectedApkToCheckedDevices) { text = "Install APK" };
        _installButton.style.width = 110f;
        _installButton.style.height = 24f;
        _installButton.style.flexShrink = 0f;
        _installButton.SetEnabled(false);
        apkRow.Add(_installButton);

        root.Add(apkRow);

        var bundleLabel = new Label("Bundle Name");
        bundleLabel.style.marginBottom = 4f;
        root.Add(bundleLabel);

        _bundleNameField = new TextField();
        _bundleNameField.style.marginBottom = 8f;
        _bundleNameField.RegisterValueChangedCallback(evt => SetBundleName(evt.newValue, persistAsLastBundleName: true));
        root.Add(_bundleNameField);

        var devicesHeaderRow = new VisualElement();
        devicesHeaderRow.style.flexDirection = FlexDirection.Row;
        devicesHeaderRow.style.justifyContent = Justify.SpaceBetween;
        devicesHeaderRow.style.alignItems = Align.Center;
        devicesHeaderRow.style.marginBottom = 6f;

        var devicesLabel = new Label("Connected Devices");
        devicesLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
        devicesHeaderRow.Add(devicesLabel);

        var refreshDevicesButton = new Button(RefreshDeviceList) { text = "Refresh" };
        refreshDevicesButton.style.width = 100f;
        devicesHeaderRow.Add(refreshDevicesButton);

        root.Add(devicesHeaderRow);

        _devicesScrollView = new ScrollView(ScrollViewMode.Vertical);
        _devicesScrollView.style.flexGrow = 1f;
        _devicesScrollView.style.minHeight = DeviceViewportMinHeight;
        _devicesScrollView.style.marginBottom = 8f;
        _devicesScrollView.style.borderTopWidth = 1f;
        _devicesScrollView.style.borderBottomWidth = 1f;
        _devicesScrollView.style.borderLeftWidth = 1f;
        _devicesScrollView.style.borderRightWidth = 1f;
        _devicesScrollView.style.borderTopColor = new Color(0.22f, 0.22f, 0.22f);
        _devicesScrollView.style.borderBottomColor = new Color(0.22f, 0.22f, 0.22f);
        _devicesScrollView.style.borderLeftColor = new Color(0.22f, 0.22f, 0.22f);
        _devicesScrollView.style.borderRightColor = new Color(0.22f, 0.22f, 0.22f);

        _deviceCardsContainer = new VisualElement();
        _deviceCardsContainer.style.flexDirection = FlexDirection.Row;
        _deviceCardsContainer.style.flexWrap = Wrap.Wrap;
        _deviceCardsContainer.style.justifyContent = Justify.Center;
        _deviceCardsContainer.style.alignSelf = Align.Stretch;
        _deviceCardsContainer.style.alignItems = Align.FlexStart;
        _deviceCardsContainer.style.alignContent = Align.FlexStart;
        _deviceCardsContainer.style.paddingLeft = 0f;
        _deviceCardsContainer.style.paddingTop = 6f;
        _deviceCardsContainer.style.paddingRight = 0f;
        _deviceCardsContainer.style.paddingBottom = 6f;
        _devicesScrollView.Add(_deviceCardsContainer);

        root.Add(_devicesScrollView);

        _statusLabel = new Label();
        _statusLabel.style.marginBottom = 8f;
        root.Add(_statusLabel);

    }

    private void OnSelectApkClicked()
    {
        var initialDirectory = GetInitialApkDirectory();
        var apkPath = EditorUtility.OpenFilePanel("Select APK", initialDirectory, "apk");
        if (string.IsNullOrWhiteSpace(apkPath))
            return;

        SetSelectedApkPath(apkPath, persistAsLastApk: true);
    }

    private void SetSelectedApkPath(string apkPath, bool persistAsLastApk)
    {
        var normalizedPath = string.IsNullOrWhiteSpace(apkPath) ? null : Path.GetFullPath(apkPath);
        if (!string.IsNullOrWhiteSpace(normalizedPath) && !File.Exists(normalizedPath))
            normalizedPath = null;

        _selectedApkPath = normalizedPath;
        _apkPathField.SetValueWithoutNotify(_selectedApkPath ?? string.Empty);
        _installButton.SetEnabled(!string.IsNullOrWhiteSpace(_selectedApkPath));

        if (persistAsLastApk && !string.IsNullOrWhiteSpace(_selectedApkPath))
            SettingsStorage.SetLastApkPath(_selectedApkPath);
    }

    private void RestoreLastApkPath()
    {
        var lastApkPath = SettingsStorage.GetLastApkPath();
        SetSelectedApkPath(lastApkPath, persistAsLastApk: false);
    }

    private void SetBundleName(string bundleName, bool persistAsLastBundleName)
    {
        var normalized = string.IsNullOrWhiteSpace(bundleName) ? null : bundleName.Trim();
        _bundleName = normalized;
        _bundleNameField.SetValueWithoutNotify(_bundleName ?? string.Empty);

        if (persistAsLastBundleName && !string.IsNullOrWhiteSpace(_bundleName))
            SettingsStorage.SetLastBundleName(_bundleName);
    }

    private void RestoreBundleName()
    {
        var bundleName = SettingsStorage.GetLastBundleName();
        if (string.IsNullOrWhiteSpace(bundleName))
            bundleName = GetProjectBundleName();

        SetBundleName(bundleName, persistAsLastBundleName: true);
    }

    private static string GetProjectBundleName()
    {
        var current = PlayerSettings.applicationIdentifier;
        if (!string.IsNullOrWhiteSpace(current))
            return current.Trim();

        var company = SanitizeBundleToken(PlayerSettings.companyName);
        var product = SanitizeBundleToken(PlayerSettings.productName);
        return company + "." + product;
    }

    private static string SanitizeBundleToken(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "app";

        var token = new string(value
            .ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '.')
            .ToArray());

        token = token.Trim('.');
        while (token.Contains(".."))
            token = token.Replace("..", ".");

        return string.IsNullOrWhiteSpace(token) ? "app" : token;
    }

    private void RefreshDeviceList()
    {
        _deviceSelections.Clear();
        _deviceCardsContainer.Clear();

        if (!ScrSpyHandler.TryGetToolPaths(out var workingDirectory, out var adbPath, out var scrcpyPath, out var resolveError))
        {
            SetStatus(resolveError, isError: true);
            return;
        }

        try
        {
            var adbHandler = new AdbHandler(adbPath, workingDirectory);
            adbHandler.StartServer();
            var devices = adbHandler.GetConnectedDevices();
            if (devices.Count == 0)
            {
                SetStatus("No connected Android devices found.", isError: false);
                return;
            }

            EditorUtility.DisplayProgressBar("Devices", "Reading device info...", 0f);
            foreach (var device in devices)
            {
                var progress = (float)(_deviceSelections.Count + 1) / devices.Count;
                EditorUtility.DisplayProgressBar("Devices", "Reading " + device.DisplayName, progress);
                var deviceCard = BuildDeviceCardData(adbPath, workingDirectory, device);
                AddDeviceCard(deviceCard);
            }

            SetStatus("Devices found: " + devices.Count, isError: false);
        }
        catch (Exception exception)
        {
            SetStatus("Failed to refresh devices: " + exception.Message, isError: true);
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    private DeviceCardData BuildDeviceCardData(string adbPath, string workingDirectory, AdbDeviceInfo device)
    {
        var deviceHandler = new AdbHandler(adbPath, workingDirectory, device.Id);

        var model = deviceHandler.TryGetModel();
        if (string.IsNullOrWhiteSpace(model))
            model = device.Model;
        if (string.IsNullOrWhiteSpace(model))
            model = "Unknown model";

        var androidVersion = deviceHandler.TryGetAndroidVersion();
        if (string.IsNullOrWhiteSpace(androidVersion))
            androidVersion = "Unknown";

        var batteryLevel = deviceHandler.TryGetBatteryLevel();

        return new DeviceCardData(device, model, androidVersion, batteryLevel);
    }

    private void AddDeviceCard(DeviceCardData cardData)
    {
        var card = new VisualElement();
        card.style.flexDirection = FlexDirection.Column;
        card.style.width = DeviceCardWidth;
        card.style.height = DeviceCardHeight;
        card.style.marginLeft = 5f;
        card.style.marginRight = 5f;
        card.style.marginBottom = 10f;
        card.style.paddingLeft = 8f;
        card.style.paddingRight = 8f;
        card.style.paddingTop = 8f;
        card.style.paddingBottom = 8f;
        card.style.borderTopWidth = 1f;
        card.style.borderBottomWidth = 1f;
        card.style.borderLeftWidth = 1f;
        card.style.borderRightWidth = 1f;
        card.style.borderTopColor = new Color(0.22f, 0.22f, 0.22f);
        card.style.borderBottomColor = new Color(0.22f, 0.22f, 0.22f);
        card.style.borderLeftColor = new Color(0.22f, 0.22f, 0.22f);
        card.style.borderRightColor = new Color(0.22f, 0.22f, 0.22f);
        card.style.backgroundColor = new Color(0.14f, 0.16f, 0.2f, 0.45f);

        var headerRow = new VisualElement();
        headerRow.style.flexDirection = FlexDirection.Row;
        headerRow.style.alignItems = Align.Center;
        headerRow.style.marginBottom = 8f;

        var toggle = new Toggle();
        toggle.SetValueWithoutNotify(true);
        toggle.style.marginRight = 8f;
        headerRow.Add(toggle);

        var selectedLabel = new Label();
        selectedLabel.style.flexGrow = 1f;
        headerRow.Add(selectedLabel);

        var openSessionsButton = new Button(() => OpenSessionsFolderForDevice(cardData.Device));
        openSessionsButton.tooltip = "Open sessions folder";
        openSessionsButton.style.width = 22f;
        openSessionsButton.style.height = 22f;
        openSessionsButton.style.flexShrink = 0f;
        openSessionsButton.style.backgroundColor = new Color(0.18f, 0.2f, 0.24f, 0.9f);
        openSessionsButton.style.borderTopWidth = 1f;
        openSessionsButton.style.borderBottomWidth = 1f;
        openSessionsButton.style.borderLeftWidth = 1f;
        openSessionsButton.style.borderRightWidth = 1f;
        openSessionsButton.style.borderTopColor = new Color(0.22f, 0.22f, 0.22f);
        openSessionsButton.style.borderBottomColor = new Color(0.22f, 0.22f, 0.22f);
        openSessionsButton.style.borderLeftColor = new Color(0.22f, 0.22f, 0.22f);
        openSessionsButton.style.borderRightColor = new Color(0.22f, 0.22f, 0.22f);
        openSessionsButton.style.borderTopLeftRadius = 4f;
        openSessionsButton.style.borderTopRightRadius = 4f;
        openSessionsButton.style.borderBottomLeftRadius = 4f;
        openSessionsButton.style.borderBottomRightRadius = 4f;
        openSessionsButton.style.paddingLeft = 1f;
        openSessionsButton.style.paddingRight = 1f;
        openSessionsButton.style.paddingTop = 1f;
        openSessionsButton.style.paddingBottom = 1f;

        var folderIcon = EditorGUIUtility.IconContent("d_Folder Icon").image as Texture2D;
        if (folderIcon == null)
            folderIcon = EditorGUIUtility.IconContent("Folder Icon").image as Texture2D;
        if (folderIcon != null)
        {
            openSessionsButton.text = string.Empty;

            var iconImage = new Image
            {
                image = folderIcon,
                scaleMode = ScaleMode.ScaleToFit
            };
            iconImage.style.width = 20f;
            iconImage.style.height = 20f;
            iconImage.style.marginLeft = 1f;
            iconImage.style.marginTop = 1f;
            iconImage.style.marginRight = 1f;
            iconImage.style.marginBottom = 1f;
            openSessionsButton.Add(iconImage);
        }
        else
        {
            openSessionsButton.text = "...";
        }

        headerRow.Add(openSessionsButton);

        void UpdateSelectionState(bool isSelected)
        {
            selectedLabel.text = isSelected ? "Active" : "Disabled";
            selectedLabel.style.color = isSelected
                ? new Color(0.25f, 0.86f, 0.45f)
                : new Color(0.82f, 0.82f, 0.86f);
        }

        toggle.RegisterValueChangedCallback(changeEvent => UpdateSelectionState(changeEvent.newValue));
        UpdateSelectionState(toggle.value);

        card.Add(headerRow);

        var modelLabel = new Label(cardData.Model);
        modelLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
        modelLabel.style.marginBottom = 4f;
        card.Add(modelLabel);

        var androidLabel = new Label("Android: " + cardData.AndroidVersion);
        androidLabel.style.marginBottom = 2f;
        card.Add(androidLabel);

        var batteryText = cardData.BatteryLevel.HasValue
            ? cardData.BatteryLevel.Value + "%"
            : "N/A";
        var batteryLabel = new Label("Battery: " + batteryText);
        batteryLabel.style.marginBottom = 6f;
        card.Add(batteryLabel);

        var idLabel = new Label(cardData.Device.Id);
        idLabel.style.flexGrow = 1f;
        idLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
        idLabel.style.color = new Color(0.7f, 0.7f, 0.75f);
        idLabel.style.marginBottom = 8f;
        card.Add(idLabel);

        var spacer = new VisualElement();
        spacer.style.flexGrow = 1f;
        card.Add(spacer);

        var sessionRunning = IsSessionRunning(cardData.Device.Id);
        var sessionStateLabel = new Label("Session: " + (sessionRunning ? "Running" : "Idle"));
        sessionStateLabel.style.marginBottom = 6f;
        sessionStateLabel.style.color = sessionRunning
            ? new Color(0.25f, 0.86f, 0.45f)
            : new Color(0.82f, 0.82f, 0.86f);
        card.Add(sessionStateLabel);

        var actionRowTop = new VisualElement();
        actionRowTop.style.flexDirection = FlexDirection.Row;
        actionRowTop.style.alignItems = Align.Center;
        actionRowTop.style.marginBottom = 6f;

        var sessionButton = new Button(() =>
        {
            if (IsSessionRunning(cardData.Device.Id))
                StopSessionForDevice(cardData.Device);
            else
                StartSessionForDevice(cardData.Device);
        })
        {
            text = sessionRunning ? "Stop Session" : "Start Session"
        };
        sessionButton.style.height = 26f;
        sessionButton.style.flexGrow = 1f;
        actionRowTop.Add(sessionButton);

        card.Add(actionRowTop);

        var actionRowBottom = new VisualElement();
        actionRowBottom.style.flexDirection = FlexDirection.Row;
        actionRowBottom.style.alignItems = Align.Center;

        var reportBugButton = new Button(() => OpenBugReportForDevice(cardData.Device)) { text = "Stop And Report" };
        reportBugButton.style.height = 26f;
        reportBugButton.style.flexGrow = 1f;
        reportBugButton.style.marginRight = 6f;
        actionRowBottom.Add(reportBugButton);

        var screenshotButton = CreateScreenshotButton(() => CaptureScreenshotForDevice(cardData.Device));
        actionRowBottom.Add(screenshotButton);

        card.Add(actionRowBottom);

        _deviceCardsContainer.Add(card);
        _deviceSelections.Add(new DeviceSelection(cardData.Device, toggle));
    }

    private void StartSessionForDevice(AdbDeviceInfo device)
    {
        string message;
        var isError = false;
        Process logcatProcess = null;
        Process scrcpyProcess = null;

        if (!ScrSpyHandler.TryGetToolPaths(out var workingDirectory, out var adbPath, out var scrcpyPath, out var resolveError))
        {
            SetStatus(resolveError, isError: true);
            return;
        }

        try
        {
            if (IsSessionRunning(device.Id))
            {
                SetStatus("Session already running for " + device.DisplayName, isError: false);
                return;
            }

            var paths = SessionCaptureStorage.CreateSessionPaths(device);
            var bootstrap = new AdbHandler(adbPath, workingDirectory, device.Id);
            bootstrap.StartServer();
            bootstrap.WaitForDevice();
            var model = bootstrap.TryGetModel();
            if (string.IsNullOrWhiteSpace(model))
                model = device.Model;
            if (string.IsNullOrWhiteSpace(model))
                model = "Unknown";

            var androidVersion = bootstrap.TryGetAndroidVersion();
            if (string.IsNullOrWhiteSpace(androidVersion))
                androidVersion = "Unknown";

            var screenResolution = bootstrap.TryGetScreenResolution();
            if (string.IsNullOrWhiteSpace(screenResolution))
                screenResolution = "Unknown";

            var batteryLevel = bootstrap.TryGetBatteryLevel();

            int? processId = null;
            if (!string.IsNullOrWhiteSpace(_bundleName))
            {
                bootstrap.UnlockDeviceAndSwipeUp();
                bootstrap.LaunchInstalledApp(_bundleName);
                Thread.Sleep(700);
                processId = bootstrap.TryGetProcessId(_bundleName);
                AndroidLogcatHandler.OpenAndroidLogcat(device.Id, _bundleName);
            }
            else
            {
                Debug.LogWarning("Bundle Name is empty. Session will capture unfiltered logcat stream. Recommended: specify Bundle Name.");
            }

            logcatProcess = StartLogCapture(adbPath, workingDirectory, device.Id, _bundleName, paths.LogFilePath, paths.UnityLogFilePath, processId);
            if (logcatProcess.HasExited)
                throw new InvalidOperationException("adb logcat exited immediately.");

            scrcpyProcess = ScrSpyHandler.StartSessionRecording(scrcpyPath, adbPath, workingDirectory, paths.VideoFilePath, device.Id);
            if (scrcpyProcess.HasExited)
                throw new InvalidOperationException("scrcpy exited immediately. Check device connection and USB debugging.");

            _sessionsByDeviceId[device.Id] = new SessionRuntimeState(
                device,
                model,
                androidVersion,
                screenResolution,
                batteryLevel,
                _bundleName,
                paths,
                logcatProcess,
                scrcpyProcess);
            message = "Session started: " + paths.SessionDirectory;
            Debug.Log("Session started for " + device.DisplayName + ". Folder: " + paths.SessionDirectory);
        }
        catch (Exception exception)
        {
            StopProcess(logcatProcess);
            StopProcess(scrcpyProcess);
            message = "Failed to start session for " + device.DisplayName + ": " + exception.Message;
            isError = true;
            Debug.LogError(message);
        }

        RefreshDeviceList();
        SetStatus(message, isError);
    }

    private void OpenSessionsFolderForDevice(AdbDeviceInfo device)
    {
        try
        {
            var deviceDirectory = SessionCaptureStorage.GetDeviceSessionsDirectory(device, createIfMissing: true);
            OpenFolderPath(deviceDirectory);
            SetStatus("Opened sessions folder: " + device.DisplayName, isError: false);
        }
        catch (Exception exception)
        {
            SetStatus("Failed to open sessions folder: " + exception.Message, isError: true);
        }
    }

    private void OpenBugReportForDevice(AdbDeviceInfo device)
    {
        if (!ScrSpyHandler.TryGetToolPaths(out var workingDirectory, out var adbPath, out _, out var resolveError))
        {
            SetStatus(resolveError, isError: true);
            return;
        }

        try
        {
            if (_sessionsByDeviceId.TryGetValue(device.Id, out var sessionState) && sessionState.ScrcpyProcess != null && !sessionState.ScrcpyProcess.HasExited)
            {
                StopAndFinalizeSession(device.Id, sessionState, logSummary: true);
                RefreshDeviceList();

                _selectedSessionDirectoryByDeviceId[device.Id] = sessionState.Paths.SessionDirectory;
                var screenshotPath = TryFindLatestScreenshotPath(sessionState.Paths.SessionDirectory);
                BugReportWindow.Open(screenshotPath, sessionState.Paths.SessionDirectory);
                SetStatus("Session stopped and bug report opened for " + device.DisplayName, isError: false);
                return;
            }

            var selectedSessionDirectory = PromptAndRememberSessionDirectory(device);
            if (string.IsNullOrWhiteSpace(selectedSessionDirectory))
            {
                SetStatus("Session folder selection canceled.", isError: false);
                return;
            }

            var screenshotPathFromFolder = TryFindLatestScreenshotPath(selectedSessionDirectory);
            BugReportWindow.Open(screenshotPathFromFolder, selectedSessionDirectory);
            SetStatus("Bug report opened using selected session folder.", isError: false);
        }
        catch (Exception exception)
        {
            SetStatus("Failed to open bug report: " + exception.Message, isError: true);
        }
    }

    private void CaptureScreenshotForDevice(AdbDeviceInfo device)
    {
        if (!ScrSpyHandler.TryGetToolPaths(out var workingDirectory, out var adbPath, out _, out var resolveError))
        {
            SetStatus(resolveError, isError: true);
            return;
        }

        try
        {
            SetStatus("Capturing screenshot: " + device.DisplayName + "...", isError: false);
            EditorUtility.DisplayProgressBar("Screenshot", "Capturing screenshot from " + device.DisplayName, 0.5f);

            string targetDirectory;
            if (_sessionsByDeviceId.TryGetValue(device.Id, out var sessionState) && sessionState.ScrcpyProcess != null && !sessionState.ScrcpyProcess.HasExited)
            {
                targetDirectory = sessionState.Paths.SessionDirectory;
            }
            else if (_selectedSessionDirectoryByDeviceId.TryGetValue(device.Id, out var rememberedDirectory) && Directory.Exists(rememberedDirectory))
            {
                targetDirectory = rememberedDirectory;
            }
            else
            {
                SetStatus("No active session folder for screenshot. Start session or select session folder first.", isError: true);
                return;
            }

            var screenshotPath = GetUniqueFilePath(targetDirectory, "screenshot", ".png");
            var adbHandler = new AdbHandler(adbPath, workingDirectory, device.Id);
            adbHandler.CaptureScreenshot(screenshotPath);
            SetStatus("Screenshot saved: " + screenshotPath, isError: false);
        }
        catch (Exception exception)
        {
            SetStatus("Failed to capture screenshot: " + exception.Message, isError: true);
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    private static Button CreateScreenshotButton(Action onClick)
    {
        var button = new Button(onClick);
        button.tooltip = "Take screenshot";
        button.style.width = 46f;
        button.style.height = 26f;
        button.style.flexShrink = 0f;
        button.style.backgroundColor = new Color(0.48f, 0.24f, 0.72f, 0.95f);
        button.style.borderTopWidth = 1f;
        button.style.borderBottomWidth = 1f;
        button.style.borderLeftWidth = 1f;
        button.style.borderRightWidth = 1f;
        button.style.borderTopColor = new Color(0.62f, 0.4f, 0.82f);
        button.style.borderBottomColor = new Color(0.62f, 0.4f, 0.82f);
        button.style.borderLeftColor = new Color(0.62f, 0.4f, 0.82f);
        button.style.borderRightColor = new Color(0.62f, 0.4f, 0.82f);
        button.style.borderTopLeftRadius = 4f;
        button.style.borderTopRightRadius = 4f;
        button.style.borderBottomLeftRadius = 4f;
        button.style.borderBottomRightRadius = 4f;
        button.style.paddingLeft = 0f;
        button.style.paddingRight = 0f;
        button.style.paddingTop = 0f;
        button.style.paddingBottom = 0f;
        button.text = string.Empty;

        var icon = TryGetCameraIcon();
        if (icon != null)
        {
            var iconImage = new Image
            {
                image = icon,
                scaleMode = ScaleMode.ScaleToFit
            };
            iconImage.style.width = 18f;
            iconImage.style.height = 18f;
            iconImage.style.marginLeft = 14f;
            iconImage.style.marginTop = 4f;
            iconImage.style.marginRight = 14f;
            iconImage.style.marginBottom = 4f;
            button.Add(iconImage);
        }
        else
        {
            button.text = "Cam";
            button.style.color = Color.white;
        }

        return button;
    }

    private static Texture2D TryGetCameraIcon()
    {
        var names = new[]
        {
            "d_Camera Icon",
            "Camera Icon",
            "d_SceneViewCamera",
            "SceneViewCamera"
        };

        foreach (var name in names)
        {
            var icon = EditorGUIUtility.IconContent(name).image as Texture2D;
            if (icon != null)
                return icon;
        }

        return null;
    }

    private string PromptAndRememberSessionDirectory(AdbDeviceInfo device)
    {
        var initialDirectory = string.Empty;
        if (_selectedSessionDirectoryByDeviceId.TryGetValue(device.Id, out var rememberedDirectory) && Directory.Exists(rememberedDirectory))
        {
            initialDirectory = rememberedDirectory;
        }
        else
        {
            initialDirectory = SessionCaptureStorage.GetDeviceSessionsDirectory(device, createIfMissing: true);
        }

        var selectedDirectory = EditorUtility.OpenFolderPanel("Select session folder for " + device.DisplayName, initialDirectory, string.Empty);
        if (string.IsNullOrWhiteSpace(selectedDirectory))
            return null;

        _selectedSessionDirectoryByDeviceId[device.Id] = selectedDirectory;
        return selectedDirectory;
    }

    private static string TryFindLatestScreenshotPath(string sessionDirectory)
    {
        if (string.IsNullOrWhiteSpace(sessionDirectory) || !Directory.Exists(sessionDirectory))
            return null;

        var screenshots = Directory.GetFiles(sessionDirectory, "*.png")
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .ToArray();

        return screenshots.Length == 0 ? null : screenshots[0].FullName;
    }

    private static string GetUniqueFilePath(string directory, string baseName, string extension)
    {
        var initialPath = Path.Combine(directory, baseName + extension);
        if (!File.Exists(initialPath))
            return initialPath;

        var index = 1;
        while (true)
        {
            var candidatePath = Path.Combine(directory, baseName + "_" + index + extension);
            if (!File.Exists(candidatePath))
                return candidatePath;

            index++;
        }
    }

    private void StopSessionForDevice(AdbDeviceInfo device)
    {
        if (!_sessionsByDeviceId.TryGetValue(device.Id, out var sessionState))
        {
            SetStatus("No active session for " + device.DisplayName, isError: true);
            return;
        }

        StopAndFinalizeSession(device.Id, sessionState, logSummary: true);
        RefreshDeviceList();
    }

    private static void OpenFolderPath(string folderPath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = folderPath,
            UseShellExecute = true
        };

        Process.Start(startInfo);
    }

    private bool IsSessionRunning(string deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
            return false;

        if (!_sessionsByDeviceId.TryGetValue(deviceId, out var state))
            return false;

        if (state.ScrcpyProcess == null)
            return false;

        return !state.ScrcpyProcess.HasExited;
    }

    private static Process StartLogCapture(string adbPath, string workingDirectory, string deviceId, string bundleName, string logFilePath, string unityLogFilePath, int? processId)
    {
        var args = "-s \"" + deviceId + "\" logcat";
        if (processId.HasValue)
            args += " --pid=" + processId.Value;
        else
            Debug.LogWarning("Session log capture: process id for package '" + bundleName + "' was not found. Using unfiltered adb logcat stream.");

        var startInfo = new ProcessStartInfo
        {
            FileName = adbPath,
            Arguments = args,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        var process = Process.Start(startInfo);
        if (process == null)
            throw new InvalidOperationException("Unable to start adb logcat process.");

        StartLogPump(process.StandardOutput, logFilePath, unityLogFilePath, bundleName, useBundleFilter: !processId.HasValue);
        StartLogPump(process.StandardError, logFilePath, unityLogFilePath, bundleName, useBundleFilter: !processId.HasValue);
        return process;
    }

    private static void StartLogPump(StreamReader reader, string outputPath, string unityOutputPath, string bundleName, bool useBundleFilter)
    {
        Task.Run(() =>
        {
            using (var writer = new StreamWriter(new FileStream(outputPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite), Encoding.UTF8))
            using (var unityWriter = new StreamWriter(new FileStream(unityOutputPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite), Encoding.UTF8))
            {
                while (!reader.EndOfStream)
                {
                    var line = reader.ReadLine();
                    if (line == null)
                        continue;

                    if (!useBundleFilter || string.IsNullOrWhiteSpace(bundleName) || line.IndexOf(bundleName, StringComparison.OrdinalIgnoreCase) >= 0)
                        writer.WriteLine(line);

                    if (line.IndexOf("Unity", StringComparison.OrdinalIgnoreCase) >= 0)
                        unityWriter.WriteLine(line);
                }
            }
        });
    }

    private void StopAllSessions()
    {
        var sessionIds = _sessionsByDeviceId.Keys.ToArray();
        foreach (var sessionId in sessionIds)
        {
            if (_sessionsByDeviceId.TryGetValue(sessionId, out var state))
                StopAndFinalizeSession(sessionId, state, logSummary: false);
        }
    }

    private void StopAndFinalizeSession(string deviceId, SessionRuntimeState state, bool logSummary)
    {
        string stopScreenshotPath = null;
        TryCaptureStopScreenshot(state, out stopScreenshotPath);

        StopProcess(state.LogcatProcess);
        StopProcess(state.ScrcpyProcess);

        // Give log pump tasks a brief window to flush final lines.
        Thread.Sleep(200);

        var summary = BuildSessionSummary(state, stopScreenshotPath);
        TryWriteSummaryFile(state.Paths.SessionDirectory, summary);

        if (logSummary)
        {
            Debug.Log(summary);
            SetStatus("Session stopped. Folder: " + state.Paths.SessionDirectory, isError: false);
        }

        _sessionsByDeviceId.Remove(deviceId);
    }

    private static void TryWriteSummaryFile(string sessionDirectory, string summary)
    {
        try
        {
            var summaryPath = Path.Combine(sessionDirectory, "summary.txt");
            File.WriteAllText(summaryPath, summary + Environment.NewLine, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            Debug.LogWarning("Failed to write summary.txt: " + ex.Message);
        }
    }

    private void TryCaptureStopScreenshot(SessionRuntimeState state, out string screenshotPath)
    {
        screenshotPath = null;

        try
        {
            if (!ScrSpyHandler.TryGetToolPaths(out var workingDirectory, out var adbPath, out _, out var resolveError))
            {
                Debug.LogWarning("Stop Session screenshot skipped: " + resolveError);
                return;
            }

            screenshotPath = GetUniqueFilePath(state.Paths.SessionDirectory, "screenshot_stop", ".png");
            var adbHandler = new AdbHandler(adbPath, workingDirectory, state.Device.Id);
            adbHandler.CaptureScreenshot(screenshotPath);
        }
        catch (Exception ex)
        {
            Debug.LogWarning("Stop Session screenshot failed: " + ex.Message);
            screenshotPath = null;
        }
    }

    private static string BuildSessionSummary(SessionRuntimeState state, string stopScreenshotPath)
    {
        var builder = new StringBuilder(256);
        builder.AppendLine("Session summary:");
        builder.AppendLine("Device: " + state.Device.DisplayName);
        builder.AppendLine("Device ID: " + state.Device.Id);
        builder.AppendLine("Model: " + state.Model);
        builder.AppendLine("Android: " + state.AndroidVersion);
        builder.AppendLine("Screen resolution: " + state.ScreenResolution);
        builder.AppendLine("Battery at start: " + (state.BatteryLevelAtStart.HasValue ? state.BatteryLevelAtStart.Value + "%" : "Unknown"));
        builder.AppendLine("Bundle Name: " + (string.IsNullOrWhiteSpace(state.BundleName) ? "<not specified>" : state.BundleName));
        builder.AppendLine("Started: " + state.Paths.SessionStartLocalTime.ToString("yyyy-MM-dd HH:mm:ss"));
        if (!string.IsNullOrWhiteSpace(stopScreenshotPath))
            builder.AppendLine("Stop screenshot: " + stopScreenshotPath);
        builder.AppendLine("Session folder: " + state.Paths.SessionDirectory);
        return builder.ToString().TrimEnd();
    }

    private static void StopProcess(Process process)
    {
        if (process == null)
            return;

        try
        {
            if (process.HasExited)
                return;

            process.CloseMainWindow();
            if (!process.WaitForExit(1500))
                process.Kill();
        }
        catch (Exception ex)
        {
            Debug.LogWarning("Failed to stop process: " + ex.Message);
        }
    }

    private void InstallSelectedApkToCheckedDevices()
    {
        if (string.IsNullOrWhiteSpace(_selectedApkPath) || !File.Exists(_selectedApkPath))
        {
            SetStatus("Select a valid APK file before install.", isError: true);
            return;
        }

        var selectedDevices = _deviceSelections
            .Where(selection => selection.Toggle.value)
            .Select(selection => selection.Device)
            .ToArray();

        if (selectedDevices.Length == 0)
        {
            EditorUtility.DisplayDialog("Install APK", "Select at least one device.", "OK");
            return;
        }

        if (!AaptHandler.TryGetPackageName(_selectedApkPath, out var packageName, out var packageError))
        {
            SetStatus(packageError, isError: true);
            return;
        }

        SetBundleName(packageName, persistAsLastBundleName: true);

        if (!ScrSpyHandler.TryGetToolPaths(out var workingDirectory, out var adbPath, out var scrcpyPath, out var resolveError))
        {
            SetStatus(resolveError, isError: true);
            return;
        }

        var successCount = 0;
        var failures = new List<string>();
        try
        {
            var bootstrapHandler = new AdbHandler(adbPath, workingDirectory);
            bootstrapHandler.StartServer();

            for (var index = 0; index < selectedDevices.Length; index++)
            {
                var device = selectedDevices[index];
                var progress = (index + 1f) / selectedDevices.Length;
                EditorUtility.DisplayProgressBar("Install APK", "Installing to " + device.DisplayName, progress);

                try
                {
                    var deviceHandler = new AdbHandler(adbPath, workingDirectory, device.Id);
                    deviceHandler.WaitForDevice();
                    deviceHandler.InstallApkWithFallback(_selectedApkPath, packageName);
                    deviceHandler.UnlockDeviceAndSwipeUp();
                    successCount++;
                }
                catch (Exception exception)
                {
                    failures.Add(device.DisplayName + ": " + exception.Message);
                }
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        RefreshDeviceList();

        if (failures.Count == 0)
        {
            SetStatus("APK installed on " + successCount + " device(s).", isError: false);
            return;
        }

        foreach (var failure in failures)
            Debug.LogError("Install failed: " + failure);

        SetStatus(
            "APK installed on " + successCount + "/" + selectedDevices.Length + " device(s). Check Console for errors.",
            isError: true);
    }

    private void SetStatus(string message, bool isError)
    {
        _statusLabel.text = message ?? string.Empty;
        _statusLabel.style.color = isError
            ? new Color(0.95f, 0.35f, 0.35f)
            : new Color(0.75f, 0.85f, 0.75f);
    }

    private string GetInitialApkDirectory()
    {
        if (!string.IsNullOrWhiteSpace(_selectedApkPath) && File.Exists(_selectedApkPath))
            return Path.GetDirectoryName(_selectedApkPath);

        var lastApkPath = SettingsStorage.GetLastApkPath();
        if (!string.IsNullOrWhiteSpace(lastApkPath) && File.Exists(lastApkPath))
            return Path.GetDirectoryName(lastApkPath);

        var projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
        return string.IsNullOrEmpty(projectRoot) ? Application.dataPath : projectRoot;
    }

    private readonly struct DeviceCardData
    {
        public readonly AdbDeviceInfo Device;
        public readonly string Model;
        public readonly string AndroidVersion;
        public readonly int? BatteryLevel;

        public DeviceCardData(AdbDeviceInfo device, string model, string androidVersion, int? batteryLevel)
        {
            Device = device;
            Model = model;
            AndroidVersion = androidVersion;
            BatteryLevel = batteryLevel;
        }
    }

    private readonly struct DeviceSelection
    {
        public readonly AdbDeviceInfo Device;
        public readonly Toggle Toggle;

        public DeviceSelection(AdbDeviceInfo device, Toggle toggle)
        {
            Device = device;
            Toggle = toggle;
        }
    }

    private readonly struct SessionRuntimeState
    {
        public readonly AdbDeviceInfo Device;
        public readonly string Model;
        public readonly string AndroidVersion;
        public readonly string ScreenResolution;
        public readonly int? BatteryLevelAtStart;
        public readonly string BundleName;
        public readonly SessionCapturePaths Paths;
        public readonly Process LogcatProcess;
        public readonly Process ScrcpyProcess;

        public SessionRuntimeState(
            AdbDeviceInfo device,
            string model,
            string androidVersion,
            string screenResolution,
            int? batteryLevelAtStart,
            string bundleName,
            SessionCapturePaths paths,
            Process logcatProcess,
            Process scrcpyProcess)
        {
            Device = device;
            Model = model;
            AndroidVersion = androidVersion;
            ScreenResolution = screenResolution;
            BatteryLevelAtStart = batteryLevelAtStart;
            BundleName = bundleName;
            Paths = paths;
            LogcatProcess = logcatProcess;
            ScrcpyProcess = scrcpyProcess;
        }
    }
}

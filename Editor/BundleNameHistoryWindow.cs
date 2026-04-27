using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

internal sealed class BundleNameHistoryWindow : EditorWindow
{
    private AndroidInstallWindow _owner;
    private Vector2 _scrollPosition;
    private string _newBundleName;

    public static void Open(AndroidInstallWindow owner)
    {
        var window = GetWindow<BundleNameHistoryWindow>(utility: true, title: "Bundle Names");
        window.minSize = new Vector2(460f, 300f);
        window._owner = owner;
        window.Show();
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Bundle Names", EditorStyles.boldLabel);
        EditorGUILayout.Space(6f);

        DrawAddRow();
        EditorGUILayout.Space(8f);
        DrawTable();
        GUILayout.FlexibleSpace();

        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Close", GUILayout.Width(100f)))
                Close();
        }
    }

    private void DrawAddRow()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            _newBundleName = EditorGUILayout.TextField("New", _newBundleName ?? string.Empty);

            using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(_newBundleName)))
            {
                if (GUILayout.Button("Add", GUILayout.Width(90f)))
                {
                    SettingsStorage.AddBundleNameHistory(_newBundleName);
                    _newBundleName = string.Empty;
                    NotifyOwnerChanged();
                    GUI.FocusControl(null);
                }
            }
        }
    }

    private void DrawTable()
    {
        var history = SettingsStorage.GetBundleNameHistory();
        if (history.Count == 0)
        {
            EditorGUILayout.HelpBox("No saved bundle names.", MessageType.Info);
            return;
        }

        EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
        EditorGUILayout.LabelField("Bundle Name", EditorStyles.miniBoldLabel);
        GUILayout.Space(76f);
        EditorGUILayout.EndHorizontal();

        _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
        foreach (var bundleName in history)
        {
            DrawTableRow(bundleName);
        }
        EditorGUILayout.EndScrollView();
    }

    private void DrawTableRow(string bundleName)
    {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.SelectableLabel(bundleName, GUILayout.Height(EditorGUIUtility.singleLineHeight));
            if (GUILayout.Button("Delete", GUILayout.Width(70f)))
            {
                SettingsStorage.RemoveBundleNameHistory(bundleName);
                NotifyOwnerChanged();
                GUIUtility.ExitGUI();
            }
        }
    }

    private void NotifyOwnerChanged()
    {
        _owner?.RefreshBundleNameOptions();
        Repaint();
    }
}

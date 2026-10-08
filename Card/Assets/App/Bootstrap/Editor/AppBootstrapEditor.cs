#if UNITY_EDITOR
using App.Net;
using UnityEditor;
using UnityEngine;

namespace App.Bootstrap.Editor
{
    [CustomEditor(typeof(AppBootstrap))]
    public sealed class AppBootstrapEditor : UnityEditor.Editor
    {
        private SerializedProperty _enableServerConnection;
        private SerializedProperty _serverEndpoint;
        private SerializedProperty _customServerHost;
        private SerializedProperty _customServerPort;
        private SerializedProperty _allowIosHighFrameRate;

        private void OnEnable()
        {
            _enableServerConnection = serializedObject.FindProperty("enableServerConnection");
            _serverEndpoint = serializedObject.FindProperty("serverEndpoint");
            _customServerHost = serializedObject.FindProperty("customServerHost");
            _customServerPort = serializedObject.FindProperty("customServerPort");
            _allowIosHighFrameRate = serializedObject.FindProperty("allowIosHighFrameRate");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.LabelField("网络", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_enableServerConnection);
            EditorGUILayout.PropertyField(_serverEndpoint, new GUIContent("服务器地址"));

            var preset = (GameApiServerEndpoint)_serverEndpoint.enumValueIndex;
            if (preset == GameApiServerEndpoint.Custom)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_customServerHost, new GUIContent("主机 (IP/域名)"));
                EditorGUILayout.PropertyField(_customServerPort, new GUIContent("端口"));
                EditorGUI.indentLevel--;
            }

            var resolved = GameApiSettings.ResolveBaseUrl(
                preset,
                _customServerHost.stringValue,
                _customServerPort.intValue);
            var ws = BuildPreviewWebSocketUrl(resolved);
            EditorGUILayout.HelpBox(
                $"Play 时将使用：\nHTTP  {resolved}\nWS    {ws}",
                MessageType.Info);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("帧率", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_allowIosHighFrameRate);

            serializedObject.ApplyModifiedProperties();
        }

        private static string BuildPreviewWebSocketUrl(string httpBase)
        {
            var http = (httpBase ?? GameApiSettings.DefaultBaseUrl).TrimEnd('/');
            if (http.StartsWith("https://", System.StringComparison.OrdinalIgnoreCase))
            {
                return "wss://" + http.Substring("https://".Length) + "/v1/pvp/ws";
            }

            if (http.StartsWith("http://", System.StringComparison.OrdinalIgnoreCase))
            {
                return "ws://" + http.Substring("http://".Length) + "/v1/pvp/ws";
            }

            return "ws://" + http + "/v1/pvp/ws";
        }
    }
}
#endif

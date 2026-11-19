using System;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace App.UI.Popup
{
    /// <summary>平台头像、默认头像与可选等级角标。</summary>
    public sealed class PlayerRankAvatarView : MonoBehaviour
    {
        [SerializeField] private Image _fallback;
        [SerializeField] private RawImage _portrait;
        [SerializeField] private TMP_Text _level;

        private UnityWebRequest _request;
        private Texture2D _texture;

        public void SetProfile(string avatarUrl, int? level)
        {
            Clear();
            _level.transform.parent.gameObject.SetActive(level.HasValue);
            _level.text = level?.ToString() ?? string.Empty;

            if (Uri.TryCreate(avatarUrl, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
            {
                _ = LoadPortraitAsync(uri);
            }
        }

        public void Clear()
        {
            var request = _request;
            _request = null;
            request?.Abort();

            if (_portrait != null)
            {
                _portrait.texture = null;
                _portrait.enabled = false;
            }
            if (_fallback != null)
            {
                _fallback.enabled = true;
            }
            if (_texture != null)
            {
                Destroy(_texture);
                _texture = null;
            }
        }

        private async Task LoadPortraitAsync(Uri uri)
        {
            using var request = UnityWebRequestTexture.GetTexture(uri, nonReadable: true);
            _request = request;
            request.timeout = 10;

            try
            {
                var operation = request.SendWebRequest();
                while (!operation.isDone && _request == request)
                {
                    await Task.Yield();
                }

                if (_request != request || request.result != UnityWebRequest.Result.Success)
                {
                    return;
                }

                _texture = DownloadHandlerTexture.GetContent(request);
                if (_texture == null)
                {
                    return;
                }

                var side = Mathf.Min(_texture.width, _texture.height);
                var width = (float)side / _texture.width;
                var height = (float)side / _texture.height;
                _portrait.uvRect = new Rect((1f - width) / 2f, (1f - height) / 2f, width, height);
                _portrait.texture = _texture;
                _portrait.enabled = true;
                _fallback.enabled = false;
            }
            catch (InvalidOperationException)
            {
                // 平台拒绝发起请求时保留默认头像。
            }
            finally
            {
                if (_request == request)
                {
                    _request = null;
                }
            }
        }

        private void OnDestroy() => Clear();
    }
}

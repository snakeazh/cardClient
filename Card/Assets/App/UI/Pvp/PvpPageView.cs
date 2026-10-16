using System.Collections.Generic;
using System.Threading.Tasks;
using App.Resources;
using DG.Tweening;
using Framework.UI.Navigation;
using Framework.UI.View;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace App.UI
{
    /// <summary>
    /// 竞技场页。匹配成功后藏起 search，打开 searchSuccess，四名玩家从四角斜向飞入。
    /// </summary>
    [AutoScreen(AppScreenIds.PvpPage, UILayer.Page, ResResourcePaths.PvpPage)]
    public sealed class PvpPageView : ViewBase<PvpPageViewModel>
    {
        private const float FlyDuration = 0.5f;

        private Sequence _intro;
        private readonly Dictionary<string, Vector2> _homes = new Dictionary<string, Vector2>();
        private readonly Dictionary<string, Sprite> _avatars = new Dictionary<string, Sprite>();
        private readonly List<Texture> _avatarTextures = new List<Texture>();
        private readonly Dictionary<Image, string> _iconUrl = new Dictionary<Image, string>();

        private const float OffscreenPadding = 48f;

        protected override void OnBind()
        {
            ViewModel.PlaySuccessIntro = PlaySuccessIntroAsync;
            ViewModel.SeatsChanged += RefreshSeats;
            CacheHomes();

            Binding.BindText(GetNode<TMP_Text>("gradeValueNum"), ViewModel.GradeValueText);
            Binding.BindText(GetNode<TMP_Text>("SearchTime"), ViewModel.SearchTimeText);
            Binding.BindText(FindYesLabel(), ViewModel.YesText);

            Binding.BindActive(UI.GetGameObject("search"), ViewModel.ShowSearch);
            Binding.BindActive(UI.GetGameObject("searchSuccess"), ViewModel.ShowSearchSuccess);
            Binding.BindActive(GetNode<Button>("StartBtn").gameObject, ViewModel.ShowStart);
            Binding.BindActive(GetNode<Button>("CancelBtn").gameObject, ViewModel.ShowCancel);
            Binding.BindActive(GetNode<TMP_Text>("SearchTime").gameObject, ViewModel.ShowSearchTime);
            Binding.BindActive(UI.GetGameObject("MatchSuccess"), ViewModel.ShowMatchSuccess);
            Binding.BindActive(GetNode<Button>("YesBtn").gameObject, ViewModel.ShowYes);
            Binding.BindActive(GetNode<Button>("CancelPvpBtn").gameObject, ViewModel.ShowCancelPvp);

            Binding.BindCommand(GetNode<Button>("backBtn"), ViewModel.BackCommand);
            Binding.BindCommand(GetNode<Button>("StartBtn"), ViewModel.StartCommand);
            Binding.BindCommand(GetNode<Button>("CancelBtn"), ViewModel.CancelCommand);
            Binding.BindCommand(GetNode<Button>("YesBtn"), ViewModel.AcceptCommand);
            Binding.BindCommand(GetNode<Button>("CancelPvpBtn"), ViewModel.DeclineCommand);
        }

        protected override Task OnViewClose()
        {
            if (ViewModel != null)
            {
                ViewModel.PlaySuccessIntro = null;
                ViewModel.SeatsChanged -= RefreshSeats;
            }

            _intro?.Kill();
            _intro = null;
            ReleaseAvatars();
            return Task.CompletedTask;
        }

        /// <summary>全员接受后，四个头像从落点往四角外飞，约 0.5 秒。</summary>
        private Task<bool> PlaySuccessIntroAsync()
        {
            _intro?.Kill();
            var stage = UI.GetGameObject("searchSuccess").GetComponent<RectTransform>();
            Canvas.ForceUpdateCanvases();
            CacheHomes();

            var players = FindPlayers(stage);
            var seq = DOTween.Sequence().SetUpdate(true).SetLink(gameObject, LinkBehaviour.KillOnDestroy);
            for (var i = 0; i < players.Count; i++)
            {
                var player = players[i];
                if (!_homes.TryGetValue(player.gameObject.name, out var home))
                {
                    home = player.anchoredPosition;
                }

                player.DOKill();
                player.anchoredPosition = home;
                seq.Join(player
                    .DOAnchorPos(OutsideCorner(player, stage, home), FlyDuration)
                    .SetEase(Ease.InCubic)
                    .SetUpdate(true));
            }

            var done = new TaskCompletionSource<bool>();
            seq.OnComplete(() => done.TrySetResult(true));
            seq.OnKill(() => done.TrySetResult(false));
            _intro = seq;
            return done.Task;
        }

        private void CacheHomes()
        {
            if (_homes.Count > 0)
            {
                return;
            }

            var stage = UI.GetGameObject("searchSuccess").GetComponent<RectTransform>();
            var players = FindPlayers(stage);
            for (var i = 0; i < players.Count; i++)
            {
                _homes[players[i].gameObject.name] = players[i].anchoredPosition;
            }
        }

        private void RefreshSeats()
        {
            var stage = UI.GetGameObject("searchSuccess").GetComponent<RectTransform>();
            var players = FindPlayers(stage);
            var seats = ViewModel != null ? ViewModel.Seats : null;
            for (var i = 0; i < players.Count; i++)
            {
                var player = players[i];
                if (_homes.TryGetValue(player.gameObject.name, out var home))
                {
                    player.anchoredPosition = home;
                }

                var seat = seats != null && i < seats.Count ? seats[i] : null;
                ApplySeat(player, seat);
            }
        }

        private void ApplySeat(RectTransform play, PvpReadySeat seat)
        {
            var revealed = seat != null && seat.Revealed;
            var nameText = FindText(play, "name");
            if (nameText != null)
            {
                nameText.text = revealed && !string.IsNullOrEmpty(seat.NickName) ? seat.NickName : "???";
            }

            var grade = FindText(play, "grade");
            if (grade != null)
            {
                grade.text = revealed ? seat.Score.ToString() : "???";
            }

            var role = FindChild(play, "roleIcon");
            var mask = role != null ? FindChild(role, "Mask") : null;
            if (mask != null)
            {
                mask.gameObject.SetActive(!revealed);
            }

            var iconTransform = role != null ? FindChild(role, "icon") : null;
            var icon = iconTransform != null ? iconTransform.GetComponent<Image>() : null;
            if (icon == null)
            {
                return;
            }

            if (!revealed)
            {
                icon.enabled = false;
                return;
            }

            icon.enabled = true;
            LoadAvatar(icon, seat.AvatarUrl);
        }

        private void LoadAvatar(Image icon, string url)
        {
            if (string.IsNullOrEmpty(url))
            {
                icon.sprite = null;
                return;
            }

            if (_iconUrl.TryGetValue(icon, out var current) && current == url && icon.sprite != null)
            {
                return;
            }

            _iconUrl[icon] = url;
            if (_avatars.TryGetValue(url, out var cached))
            {
                icon.sprite = cached;
                return;
            }

            _ = LoadAvatarAsync(icon, url);
        }

        private async Task LoadAvatarAsync(Image icon, string url)
        {
            try
            {
                using (var request = UnityWebRequestTexture.GetTexture(url))
                {
                    var op = request.SendWebRequest();
                    while (!op.isDone)
                    {
                        await Task.Yield();
                    }

                    if (request.result != UnityWebRequest.Result.Success || icon == null)
                    {
                        return;
                    }

                    if (!_iconUrl.TryGetValue(icon, out var current) || current != url)
                    {
                        return;
                    }

                    var texture = DownloadHandlerTexture.GetContent(request);
                    if (texture == null)
                    {
                        return;
                    }

                    var sprite = Sprite.Create(
                        texture,
                        new Rect(0f, 0f, texture.width, texture.height),
                        new Vector2(0.5f, 0.5f));
                    _avatarTextures.Add(texture);
                    _avatars[url] = sprite;
                    icon.sprite = sprite;
                }
            }
            catch (System.Exception)
            {
            }
        }

        private void ReleaseAvatars()
        {
            foreach (var pair in _avatars)
            {
                if (pair.Value != null)
                {
                    UnityEngine.Object.Destroy(pair.Value);
                }
            }

            _avatars.Clear();
            for (var i = 0; i < _avatarTextures.Count; i++)
            {
                if (_avatarTextures[i] != null)
                {
                    UnityEngine.Object.Destroy(_avatarTextures[i]);
                }
            }

            _avatarTextures.Clear();
            _iconUrl.Clear();
        }

        private static TMP_Text FindText(Transform root, string name)
        {
            var child = FindChild(root, name);
            return child != null ? child.GetComponent<TMP_Text>() : null;
        }

        private static Transform FindChild(Transform root, string name)
        {
            if (root == null)
            {
                return null;
            }

            if (root.name == name)
            {
                return root;
            }

            for (var i = 0; i < root.childCount; i++)
            {
                var found = FindChild(root.GetChild(i), name);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private static List<RectTransform> FindPlayers(RectTransform stage)
        {
            var players = new List<RectTransform>(4);
            var rects = stage.GetComponentsInChildren<RectTransform>(true);
            for (var i = 0; i < rects.Length; i++)
            {
                var name = rects[i].gameObject.name;
                if (name == "play1" || name == "play2" || name == "play3" || name == "play4")
                {
                    players.Add(rects[i]);
                }
            }

            players.Sort((a, b) => string.CompareOrdinal(a.gameObject.name, b.gameObject.name));
            return players;
        }

        /// <summary>按落点所在象限，算到对应角外侧的位置。</summary>
        private static Vector2 OutsideCorner(RectTransform player, RectTransform stage, Vector2 home)
        {
            var signX = home.x < 0f ? -1f : 1f;
            var signY = home.y < 0f ? -1f : 1f;
            var half = player.rect.size * 0.5f;
            var parent = player.parent as RectTransform;
            if (stage.rect.width < 100f || parent == null)
            {
                return home + new Vector2(signX * (half.x + 900f), signY * (half.y + 1400f));
            }

            var corners = new Vector3[4];
            stage.GetWorldCorners(corners);
            Vector3 world;
            if (signX < 0f && signY > 0f)
            {
                world = corners[1];
            }
            else if (signX > 0f && signY > 0f)
            {
                world = corners[2];
            }
            else if (signX < 0f)
            {
                world = corners[0];
            }
            else
            {
                world = corners[3];
            }

            var corner = (Vector2)parent.InverseTransformPoint(world);
            return corner + new Vector2(signX * (half.x + OffscreenPadding), signY * (half.y + OffscreenPadding));
        }

        private T GetNode<T>(string key) where T : Component
        {
            var go = UI.GetGameObject(key);
            var component = go.GetComponent<T>();
            return component != null ? component : go.GetComponentInChildren<T>(true);
        }

        /// <summary>YesBtn 下有按钮文案和 tip，倒计时只写文案。</summary>
        private TMP_Text FindYesLabel()
        {
            var button = GetNode<Button>("YesBtn");
            var texts = button.GetComponentsInChildren<TMP_Text>(true);
            for (var i = 0; i < texts.Length; i++)
            {
                if (texts[i].gameObject.name == "tip")
                {
                    continue;
                }

                return texts[i];
            }

            return texts.Length > 0 ? texts[0] : null;
        }
    }
}

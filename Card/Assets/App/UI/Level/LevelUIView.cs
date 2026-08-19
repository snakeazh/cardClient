using System.Collections.Generic;
using System.Threading.Tasks;
using App.Game;
using App.Item;
using App.Resources;
using DG.Tweening;
using Framework.UI.Binding;
using Framework.UI.Navigation;
using Framework.UI.View;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace App.UI
{
    /// <summary>
    /// 选角 / 选关。节点通过 UIReference / UIBind 解析。
    /// </summary>
    [AutoScreen(AppScreenIds.LevelUI, UILayer.Page, ResResourcePaths.LevelUI)]
    public sealed class LevelUIView : ViewBase<LevelUIViewModel>
    {
        private readonly List<HeroItem> _heroItems = new List<HeroItem>();
        private readonly List<LevelItem> _levelItems = new List<LevelItem>();
        private readonly Dictionary<int, Sprite> _portraits = new Dictionary<int, Sprite>();
        private PlayerItem _playerItem;
        private RectTransform _hor;
        private Tween _horTween;

        protected override void OnBind()
        {
            _hor = UI.Get<RectTransform>("hor");
            _playerItem = _hor != null ? _hor.GetComponentInChildren<PlayerItem>(true) : null;

            Binding.BindText(GetNode<TMP_Text>("skillName"), ViewModel.SkillName);
            Binding.BindText(GetNode<TMP_Text>("skillinfo"), ViewModel.SkillInfo);
            Binding.BindText(GetNode<TMP_Text>("unlockInfo"), ViewModel.UnlockInfo);
            Binding.BindActive(GetNode<TMP_Text>("unlockInfo").gameObject, ViewModel.ShowUnlockInfo);
            Binding.BindText(GetNode<TMP_Text>("stageNum"), ViewModel.StageNum);
            Binding.BindText(GetNode<TMP_Text>("stageInfoText"), ViewModel.StageInfoText);
            BindDifficultyTip();

            Binding.BindActive(UI.GetGameObject("heroSelect"), ViewModel.ShowHeroSelect);
            Binding.BindActive(UI.GetGameObject("stageInfo"), ViewModel.ShowStageInfo);
            Binding.BindActive(UI.GetGameObject("levelSelect"), ViewModel.ShowLevelSelect);
            Binding.BindActive(GetNode<Button>("useHeroBtn").gameObject, ViewModel.ShowUseHeroBtn);
            Binding.BindActive(GetNode<Button>("unlockHeroBtn").gameObject, ViewModel.ShowUnlockHeroBtn);
            Binding.BindActive(GetNode<Button>("StartGameBtn").gameObject, ViewModel.ShowStartGameBtn);
            Binding.BindActive(GetNode<Button>("unlockLevelBtn").gameObject, ViewModel.ShowUnlockLevelBtn);

            Binding.BindCommand(GetNode<Button>("LastBtn"), ViewModel.LastBtnCommand);
            Binding.BindCommand(GetNode<Button>("useHeroBtn"), ViewModel.UseHeroCommand);
            Binding.BindCommand(GetNode<Button>("unlockHeroBtn"), ViewModel.UnlockHeroCommand);
            Binding.BindCommand(GetNode<Button>("StartGameBtn"), ViewModel.StartGameCommand);
            Binding.BindCommand(GetNode<Button>("unlockLevelBtn"), ViewModel.UnlockLevelCommand);

            SpawnHeroItems();
            SpawnLevelItems();
            ApplyHor(immediate: true);
            RefreshHeroItems();
            RefreshLevelItems();
            RefreshPreview();

            Binding.Add(ViewModel.HorX.Subscribe(_ => ApplyHor(immediate: false), emitCurrent: false));
            Binding.Add(ViewModel.SelectedHeroId.Subscribe(_ =>
            {
                RefreshHeroItems();
                RefreshPreview();
            }, emitCurrent: false));
            Binding.Add(ViewModel.SelectedLevelId.Subscribe(_ => RefreshLevelItems(), emitCurrent: false));
            Binding.Add(ViewModel.SelectedDifficulty.Subscribe(_ => RebuildLevelItems(), emitCurrent: false));
        }

        protected override async Task OnViewOpen()
        {
            await LoadPortraits();
            RefreshHeroItems();
            RefreshPreview();
        }

        protected override Task OnViewClose()
        {
            _horTween?.Kill();
            _horTween = null;
            return Task.CompletedTask;
        }

        private T GetNode<T>(string key) where T : Component
        {
            return UI.GetGameObject(key).GetComponent<T>();
        }

        private void SpawnHeroItems()
        {
            var listGo = UI.GetGameObject("heroSelect");
            var template = UI.GetGameObject("heroItem").GetComponent<HeroItem>();
            template.gameObject.SetActive(false);
            _heroItems.Clear();

            var heroes = ViewModel.Heroes;
            for (var i = 0; i < heroes.Count; i++)
            {
                var hero = heroes[i];
                var go = Object.Instantiate(template.gameObject, listGo.transform, false);
                go.name = $"heroItem_{hero.Id}";
                go.SetActive(true);
                var bind = go.GetComponent<UIBind>();
                if (bind != null)
                {
                    Object.Destroy(bind);
                }

                var item = go.GetComponent<HeroItem>();
                item.BindClick(clicked => ViewModel.SelectHero(clicked.Data.Id));
                _heroItems.Add(item);
            }
        }

        private void RebuildLevelItems()
        {
            for (var i = 0; i < _levelItems.Count; i++)
            {
                var item = _levelItems[i];
                if (item != null)
                {
                    Object.Destroy(item.gameObject);
                }
            }

            _levelItems.Clear();
            SpawnLevelItems();
            RefreshLevelItems();
        }

        private void BindDifficultyTip()
        {
            var tip = FindNamed(transform, "stageTip");
            if (tip == null)
            {
                return;
            }

            var text = tip.GetComponent<TMP_Text>();
            if (text != null)
            {
                Binding.BindText(text, ViewModel.DifficultyText);
            }

            var button = tip.GetComponent<Button>();
            if (button == null)
            {
                button = tip.gameObject.AddComponent<Button>();
                button.transition = Selectable.Transition.None;
            }

            Binding.BindCommand(button, ViewModel.NextDifficultyCommand);
        }

        private static Transform FindNamed(Transform root, string nodeName)
        {
            if (root == null)
            {
                return null;
            }

            if (root.name == nodeName)
            {
                return root;
            }

            for (var i = 0; i < root.childCount; i++)
            {
                var found = FindNamed(root.GetChild(i), nodeName);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private void SpawnLevelItems()
        {
            var listGo = UI.GetGameObject("levelSelect");
            var template = UI.GetGameObject("levelItem").GetComponent<LevelItem>();
            template.gameObject.SetActive(false);
            _levelItems.Clear();

            var stages = ViewModel.Stages;
            for (var i = 0; i < stages.Count; i++)
            {
                var stage = stages[i];
                var go = Object.Instantiate(template.gameObject, listGo.transform, false);
                go.name = $"levelItem_{stage.Id}";
                go.SetActive(true);
                var bind = go.GetComponent<UIBind>();
                if (bind != null)
                {
                    Object.Destroy(bind);
                }

                var item = go.GetComponent<LevelItem>();
                item.BindClick(clicked => ViewModel.SelectLevel(clicked.Data.Id));
                _levelItems.Add(item);
            }
        }

        private void RefreshHeroItems()
        {
            var selected = ViewModel.SelectedHeroId.Value;
            for (var i = 0; i < _heroItems.Count; i++)
            {
                var item = _heroItems[i];
                var hero = ViewModel.Heroes[i];
                _portraits.TryGetValue(hero.Id, out var portrait);
                item.Bind(hero, portrait, hero.Id == selected, ViewModel.IsHeroUnlocked(hero));
            }
        }

        private void RefreshLevelItems()
        {
            var selected = ViewModel.SelectedLevelId.Value;
            var stages = ViewModel.Stages;
            var count = Mathf.Min(_levelItems.Count, stages.Count);
            for (var i = 0; i < count; i++)
            {
                var item = _levelItems[i];
                var stage = stages[i];
                item.Bind(stage, ViewModel.IsLevelUnlocked(stage), stage.Id == selected);
            }
        }

        private void RefreshPreview()
        {
            if (_playerItem == null)
            {
                return;
            }

            var hero = App.Config.HeroConfig.Get(ViewModel.SelectedHeroId.Value);
            var unlocked = ViewModel.IsHeroUnlocked(hero);
            _playerItem.ApplyTheme(false);
            _playerItem.SetState(string.Empty);
            _portraits.TryGetValue(hero != null ? hero.Id : 0, out var portrait);
            if (unlocked && hero != null)
            {
                _playerItem.SetName(hero.Name);
                _playerItem.SetHp(hero.Hp);
                _playerItem.SetAttack(hero.HeroDamage);
                _playerItem.SetPortrait(portrait);
            }
            else
            {
                _playerItem.SetName(LevelUIViewModel.LockedText);
                _playerItem.SetHp(0);
                _playerItem.SetAttack(0);
                _playerItem.SetPortrait(portrait, locked: true);
            }
        }

        private void ApplyHor(bool immediate)
        {
            if (_hor == null)
            {
                return;
            }

            _horTween?.Kill();
            var x = ViewModel.HorX.Value;
            if (immediate)
            {
                var pos = _hor.anchoredPosition;
                pos.x = x;
                _hor.anchoredPosition = pos;
                return;
            }

            _horTween = _hor.DOAnchorPosX(x, 0.35f).SetEase(Ease.OutCubic);
        }

        private async Task LoadPortraits()
        {
            var heroes = ViewModel.Heroes;
            for (var i = 0; i < heroes.Count; i++)
            {
                var hero = heroes[i];
                var key = ResResourcePaths.RoleIcon(hero.Icon);
                if (string.IsNullOrEmpty(key))
                {
                    continue;
                }

                try
                {
                    _portraits[hero.Id] = await ViewModel.Resources.LoadAsync<Sprite>(key);
                }
                catch (System.Exception)
                {
                }
            }
        }
    }
}

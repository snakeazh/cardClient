using System.Collections.Generic;
using System.Threading.Tasks;
using App.Game;
using App.Item;
using App.Resources;
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
    /// heroSelect / levelSelect 是 ScrollRect，列表项生成到 content（网格）上。
    /// </summary>
    [AutoScreen(AppScreenIds.LevelUI, UILayer.Page, ResResourcePaths.LevelUI)]
    public sealed class LevelUIView : ViewBase<LevelUIViewModel>
    {
        private readonly List<HeroItem> _heroItems = new List<HeroItem>();
        private readonly List<ItemCard> _levelItems = new List<ItemCard>();
        private PlayerItem _playerItem;

        protected override void OnBind()
        {
            _playerItem = UI.GetGameObject("PlayerItem").GetComponentInChildren<PlayerItem>(true);

            Binding.BindText(GetNode<TMP_Text>("skillName"), ViewModel.SkillName);
            Binding.BindText(GetNode<TMP_Text>("skillinfo"), ViewModel.SkillInfo);
            Binding.BindText(GetNode<TMP_Text>("unlockHerotip"), ViewModel.UnlockInfo);
            Binding.BindText(GetNode<TMP_Text>("stageNum"), ViewModel.StageNum);
            Binding.BindText(GetNode<TMP_Text>("stageInfoText"), ViewModel.StageInfoText);
            Binding.BindText(GetNode<TMP_Text>("stageInfotip"), ViewModel.DifficultyText);

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
            RefreshHeroItems();
            RefreshLevelItems();
            RefreshPreview();

            Binding.Add(ViewModel.ShowHeroSelect.Subscribe(show =>
            {
                if (show)
                {
                    RefreshHeroItems(forceSelect: true);
                    RefreshPreview();
                }
            }, emitCurrent: false));
            Binding.Add(ViewModel.ShowLevelSelect.Subscribe(show =>
            {
                if (show)
                {
                    RefreshLevelItems(forceSelect: true);
                }
            }, emitCurrent: false));
            Binding.Add(ViewModel.SelectedHeroId.Subscribe(_ =>
            {
                RefreshHeroItems();
                RefreshPreview();
            }, emitCurrent: false));
            Binding.Add(ViewModel.SelectedLevelId.Subscribe(_ => RefreshLevelItems(), emitCurrent: false));
            Binding.Add(ViewModel.SelectedDifficulty.Subscribe(_ => RefreshLevelItems(), emitCurrent: false));
        }

        protected override Task OnViewOpen()
        {
            RefreshHeroItems();
            RefreshPreview();
            return Task.CompletedTask;
        }

        private T GetNode<T>(string key) where T : Component
        {
            return UI.GetGameObject(key).GetComponent<T>();
        }

        private void SpawnHeroItems()
        {
            var template = UI.GetGameObject("heroItem").GetComponent<HeroItem>();
            template.gameObject.SetActive(false);
            _heroItems.Clear();

            var parent = ResolveListContent("heroSelect", template.transform.parent);
            var heroes = ViewModel.Heroes;
            for (var i = 0; i < heroes.Count; i++)
            {
                var hero = heroes[i];
                var go = Object.Instantiate(template.gameObject, parent, false);
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

            RebuildListLayout(parent);
        }

        private void SpawnLevelItems()
        {
            var template = UI.GetGameObject("levelItem").GetComponent<ItemCard>();
            template.gameObject.SetActive(false);
            _levelItems.Clear();

            var parent = ResolveListContent("levelSelect", template.transform.parent);
            var stages = ViewModel.Stages;
            for (var i = 0; i < stages.Count; i++)
            {
                var stage = stages[i];
                var go = Object.Instantiate(template.gameObject, parent, false);
                go.name = $"levelItem_{stage.Id}";
                go.SetActive(true);
                var bind = go.GetComponent<UIBind>();
                if (bind != null)
                {
                    Object.Destroy(bind);
                }

                var item = go.GetComponent<ItemCard>();
                var levelId = stage.Id;
                item.Clicked += _ => ViewModel.SelectLevel(levelId);
                _levelItems.Add(item);
            }

            RebuildListLayout(parent);
        }

        private Transform ResolveListContent(string listKey, Transform fallback)
        {
            var listGo = UI.GetGameObject(listKey);
            if (listGo != null)
            {
                var scroll = listGo.GetComponent<ScrollRect>();
                if (scroll != null && scroll.content != null)
                {
                    return scroll.content;
                }

                return listGo.transform;
            }

            return fallback;
        }

        private static void RebuildListLayout(Transform parent)
        {
            var content = parent as RectTransform;
            if (content != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(content);
            }
        }

        private void RefreshHeroItems(bool forceSelect = false)
        {
            var selected = ViewModel.SelectedHeroId.Value;
            for (var i = 0; i < _heroItems.Count; i++)
            {
                var item = _heroItems[i];
                var hero = ViewModel.Heroes[i];
                item.Bind(hero, PortraitLoader.GetRole(hero.Icon), hero.Id == selected, ViewModel.IsHeroUnlocked(hero), forceSelect);
            }
        }

        private void RefreshLevelItems(bool forceSelect = false)
        {
            var selected = ViewModel.SelectedLevelId.Value;
            var stages = ViewModel.Stages;
            var count = Mathf.Min(_levelItems.Count, stages.Count);
            for (var i = 0; i < count; i++)
            {
                var item = _levelItems[i];
                var stage = stages[i];
                var unlocked = ViewModel.IsLevelUnlocked(stage);
                item.SetUnlocked(unlocked);
                if (unlocked)
                {
                    item.SetName($"难度{stage.Difficulty}");
                }

                item.PlaySelected(stage.Id == selected, forceSelect);
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
            _playerItem.ApplyTheme();
            var portrait = hero != null ? PortraitLoader.GetRole(hero.Icon) : null;
            if (unlocked && hero != null)
            {
                _playerItem.SetName(hero.Name);
                _playerItem.SetHp(hero.Hp);
                _playerItem.SetAttack(hero.HeroDamage);
                _playerItem.SetPortrait(portrait);
                _playerItem.SetUnlocked(true);
            }
            else
            {
                _playerItem.SetName(LevelUIViewModel.LockedText);
                _playerItem.SetPortrait(portrait, locked: true);
                _playerItem.SetUnlocked(false);
            }
        }
    }
}

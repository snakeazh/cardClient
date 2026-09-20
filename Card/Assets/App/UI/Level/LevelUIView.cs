using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using App.Game;
using App.Item;
using App.Resources;
using Framework.Log;
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
    /// heroSelect 是 ScrollRect，列表项生成到 content（网格）上；
    /// levelSelect 换用 IevelItem 难度卡后网格已移除，模板取 content 第一个子节点，行布局由代码排布。
    /// </summary>
    [AutoScreen(AppScreenIds.LevelUI, UILayer.Page, ResResourcePaths.LevelUI)]
    public sealed class LevelUIView : ViewBase<LevelUIViewModel>
    {
        // 难度卡布局常量（IevelItem 卡面 168×173），需要微调时改这里按差值挪
        private const float LevelCardWidth = 168f;
        private const float LevelCardSpacingX = 55f;
        private const float LevelRowTopOffset = 200f;

        private readonly List<HeroItem> _heroItems = new List<HeroItem>();
        private readonly List<LevelItemCard> _levelItems = new List<LevelItemCard>();
        private PlayerItem _playerItem;
        private Coroutine _scrollHeroRoutine;
        private Coroutine _scrollLevelRoutine;

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

        protected override Task OnViewClose()
        {
            // 视图实例随界面关闭销毁，列表项回收进池供下次打开复用。
            for (var i = 0; i < _heroItems.Count; i++)
            {
                LevelItemPool.ReleaseHero(_heroItems[i]);
            }

            _heroItems.Clear();

            for (var i = 0; i < _levelItems.Count; i++)
            {
                LevelItemPool.ReleaseLevel(_levelItems[i]);
            }

            _levelItems.Clear();
            _playerItem = null;
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
                var item = LevelItemPool.RentHero(template, parent);
                item.gameObject.name = $"heroItem_{hero.Id}";
                var bind = item.GetComponent<UIBind>();
                if (bind != null)
                {
                    Object.Destroy(bind);
                }

                item.BindClick(clicked => ViewModel.SelectHero(clicked.Data.Id));
                _heroItems.Add(item);
            }

            RebuildListLayout(parent);
        }

        private void SpawnLevelItems()
        {
            // levelItem 的 UIReference 注册表指向旧实例已悬空（换模板时丢了），
            // 模板改从 levelSelect content 的第一个子节点取。
            var content = UI.GetGameObject("levelSelect").GetComponent<ScrollRect>().content;
            var template = content.childCount > 0
                ? content.GetChild(0).GetComponent<LevelItemCard>()
                : null;
            if (template == null)
            {
                // 预制体与代码版本不匹配（如 bundle 未重打/缓存未更新）时不再空引用炸掉整个界面；
                // 同时把旧预制体里美术预放的占位卡全部藏掉，避免旧关卡卡直接显示在界面上。
                AppLog.Warn(LogChannel.UI, "LevelUI 未找到 LevelItemCard 模板（levelSelect content 首子节点），跳过难度列表生成。");
                for (var i = 0; i < content.childCount; i++)
                {
                    content.GetChild(i).gameObject.SetActive(false);
                }

                return;
            }

            template.gameObject.SetActive(false);
            _levelItems.Clear();

            // 编辑器在 content 上挂了 LayoutGroup（如 GridLayoutGroup）就由它接管布局；
            // 否则走代码排布，并把没有 LayoutGroup 供源的 ContentSizeFitter 清掉（会把 content 高度压成 0）。
            var useLayoutGroup = content.GetComponent<LayoutGroup>() != null;
            if (!useLayoutGroup)
            {
                var fitter = content.GetComponent<ContentSizeFitter>();
                if (fitter != null)
                {
                    Object.Destroy(fitter);
                }
            }

            var stages = ViewModel.Stages;
            for (var i = 0; i < stages.Count; i++)
            {
                var stage = stages[i];
                var item = LevelItemPool.RentLevel(template, content);
                item.gameObject.name = $"levelItem_{stage.Id}";
                var bind = item.GetComponent<UIBind>();
                if (bind != null)
                {
                    Object.Destroy(bind);
                }

                item.ClearClicked();
                var levelId = stage.Id;
                item.Clicked += _ => ViewModel.SelectLevel(levelId);
                _levelItems.Add(item);
            }

            // 克隆完立即重建布局：网格当帧就把克隆体 rect 算好，
            // 否则首次 SetSelected 读不到卡高，抬升量会走兜底值
            RebuildListLayout(content);

            if (!useLayoutGroup)
            {
                LayoutLevelRow(content);
            }
        }

        /// <summary>难度卡单行排布：以 content 顶边为基准，水平居中、卡中心距顶 LevelRowTopOffset。</summary>
        private void LayoutLevelRow(RectTransform content)
        {
            var count = _levelItems.Count;
            if (count == 0)
            {
                return;
            }

            var step = LevelCardWidth + LevelCardSpacingX;
            for (var i = 0; i < count; i++)
            {
                var rt = _levelItems[i].transform as RectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = new Vector2((i - (count - 1) * 0.5f) * step, -LevelRowTopOffset);
            }
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
            RectTransform selectedRt = null;
            for (var i = 0; i < _heroItems.Count; i++)
            {
                var item = _heroItems[i];
                var hero = ViewModel.Heroes[i];
                var unlocked = ViewModel.IsHeroUnlocked(hero);
                var stats = ViewModel.GetHeroPanelStats(hero);
                var isSelected = hero.Id == selected;
                item.Bind(hero, PortraitLoader.GetRole(hero.Icon), isSelected, unlocked, stats, forceSelect);
                if (isSelected)
                {
                    selectedRt = item.transform as RectTransform;
                }
            }

            ScrollListToChild("heroSelect", selectedRt);
        }

        private void RefreshLevelItems(bool forceSelect = false)
        {
            var selected = ViewModel.SelectedLevelId.Value;
            var stages = ViewModel.Stages;
            var count = Mathf.Min(_levelItems.Count, stages.Count);
            RectTransform selectedRt = null;
            for (var i = 0; i < count; i++)
            {
                var item = _levelItems[i];
                var stage = stages[i];
                item.SetUnlocked(ViewModel.IsLevelUnlocked(stage));
                item.SetDifficulty(stage.Difficulty);
                var isSelected = stage.Id == selected;
                item.SetSelected(isSelected, forceSelect);
                if (isSelected)
                {
                    selectedRt = item.transform as RectTransform;
                }
            }

            ScrollListToChild("levelSelect", selectedRt);
        }

        private void ScrollListToChild(string listKey, RectTransform child)
        {
            if (child == null)
            {
                return;
            }

            if (listKey == "heroSelect")
            {
                if (_scrollHeroRoutine != null)
                {
                    StopCoroutine(_scrollHeroRoutine);
                }

                _scrollHeroRoutine = StartCoroutine(ScrollListToChildNextFrame(listKey, child));
                return;
            }

            if (listKey == "levelSelect")
            {
                if (_scrollLevelRoutine != null)
                {
                    StopCoroutine(_scrollLevelRoutine);
                }

                _scrollLevelRoutine = StartCoroutine(ScrollListToChildNextFrame(listKey, child));
            }
        }

        private IEnumerator ScrollListToChildNextFrame(string listKey, RectTransform child)
        {
            // 等一帧：面板刚激活 / ContentSizeFitter 算完后再滚。
            yield return null;
            if (child == null)
            {
                yield break;
            }

            var listGo = UI.GetGameObject(listKey);
            if (listGo == null || !listGo.activeInHierarchy)
            {
                yield break;
            }

            EnsureChildVisible(listGo.GetComponent<ScrollRect>(), child);
        }

        /// <summary>
        /// 竖向 ScrollRect：把子项滚进可视区（已在可视区内则不动）。
        /// </summary>
        private static void EnsureChildVisible(ScrollRect scroll, RectTransform child)
        {
            if (scroll == null || child == null || !scroll.vertical)
            {
                return;
            }

            var content = scroll.content;
            var viewport = scroll.viewport != null ? scroll.viewport : scroll.transform as RectTransform;
            if (content == null || viewport == null)
            {
                return;
            }

            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(content);

            var bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(viewport, child);
            var view = viewport.rect;
            float offset = 0f;
            if (bounds.max.y > view.yMax)
            {
                offset = bounds.max.y - view.yMax;
            }
            else if (bounds.min.y < view.yMin)
            {
                offset = bounds.min.y - view.yMin;
            }
            else
            {
                return;
            }

            scroll.StopMovement();
            var pos = content.anchoredPosition;
            pos.y -= offset;
            var overflow = content.rect.height - viewport.rect.height;
            if (overflow > 0f)
            {
                pos.y = Mathf.Clamp(pos.y, 0f, overflow);
            }
            else
            {
                pos.y = 0f;
            }

            content.anchoredPosition = pos;
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
                var stats = ViewModel.GetHeroPanelStats(hero);
                _playerItem.SetName(hero.Name);
                _playerItem.SetHp(stats.Hp);
                _playerItem.SetAttack(stats.Attack);
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

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
    /// 注册在 <see cref="UILayer.Popup"/>：从 Home 打开时浮在 Home 上方（Home 留在 Page 栈里不隐藏），
    /// 遮罩与内容容器由 <see cref="LevelUITransition"/> 在运行时生成。
    /// </summary>
    [AutoScreen(AppScreenIds.LevelUI, UILayer.Popup, ResResourcePaths.LevelUI)]
    public sealed class LevelUIView : ViewBase<LevelUIViewModel>
    {
        // 难度卡布局常量（IevelItem 卡面 168×173），需要微调时改这里按差值挪
        private const float LevelCardWidth = 168f;
        private const float LevelCardSpacingX = 55f;
        private const float LevelRowTopOffset = 200f;

        // 技能描述区（skillinfo）：预制体给的是 472×190 的固定框、字号固定 36，
        // 而 SkillInfo 的行数随英雄配置变化（描述若干行 + 空行 + 属性两行 + 最高纪录一行）。
        // 描述一长就会顶出框外，直接压到下方「难度X 关卡Y」大标签上，两层字叠在一起。
        // 这里把文本区顶边钉住、底边撑到难度标签上方，再交给 TMP 自动缩放兜底，
        // 任意长度的描述都收在可用区域内，不再和标签叠字。
        private const float SkillInfoMaxFontSize = 36f;
        private const float SkillInfoMinFontSize = 20f;
        private const float SkillInfoToStageLabelGap = 12f;

        private readonly List<HeroItem> _heroItems = new List<HeroItem>();
        private readonly List<LevelItemCard> _levelItems = new List<LevelItemCard>();
        private PlayerItem _playerItem;
        private Coroutine _scrollHeroRoutine;
        private Coroutine _scrollLevelRoutine;
        private LevelUITransition _transition;
        private Button _lastBtn;
        private float _inputUnblockAt;
        private float _closeAt;
        private bool _entrancePlayed;
        private bool _closing;
        private int _lastAnimatedHeroId;
        private int _lastAnimatedLevelId;
        private int _lastAnimatedDifficulty;
        private float _suppressSelectionUntil;

        protected override void OnBind()
        {
            _playerItem = UI.GetGameObject("PlayerItem").GetComponentInChildren<PlayerItem>(true);

            Binding.BindText(GetNode<TMP_Text>("skillName"), ViewModel.SkillName);
            Binding.BindText(GetNode<TMP_Text>("skillinfo"), ViewModel.SkillInfo);
            Binding.BindText(GetNode<TMP_Text>("unlockHerotip"), ViewModel.UnlockInfo);
            Binding.BindText(GetNode<TMP_Text>("stageNum"), ViewModel.StageNum);
            Binding.BindText(GetNode<TMP_Text>("stageInfoText"), ViewModel.StageInfoText);
            Binding.BindText(GetNode<TMP_Text>("stageInfotip"), ViewModel.DifficultyText);

            // 描述区先按最终文字量把可用高度算好，再走 TMP 自动缩放（见 FitSkillInfoArea）。
            FitSkillInfoArea();

            // heroSelect / stageInfo / levelSelect 的显隐改由 LevelUITransition 做动画，
            // 见下方订阅；这里不再用 BindActive 瞬切。
            Binding.BindActive(GetNode<Button>("useHeroBtn").gameObject, ViewModel.ShowUseHeroBtn);
            Binding.BindActive(GetNode<Button>("unlockHeroBtn").gameObject, ViewModel.ShowUnlockHeroBtn);
            Binding.BindActive(GetNode<Button>("StartGameBtn").gameObject, ViewModel.ShowStartGameBtn);
            Binding.BindActive(GetNode<Button>("unlockLevelBtn").gameObject, ViewModel.ShowUnlockLevelBtn);

            Binding.BindCommand(GetNode<Button>("useHeroBtn"), ViewModel.UseHeroCommand);
            Binding.BindCommand(GetNode<Button>("unlockHeroBtn"), ViewModel.UnlockHeroCommand);
            Binding.BindCommand(GetNode<Button>("StartGameBtn"), ViewModel.StartGameCommand);
            Binding.BindCommand(GetNode<Button>("unlockLevelBtn"), ViewModel.UnlockLevelCommand);

            // LastBtn 不直接 BindCommand：Hero 阶段要先播退场再执行关闭命令，
            // Stage 阶段仍是立即返回选角（见 OnLastClicked）。
            _lastBtn = GetNode<Button>("LastBtn");
            _lastBtn.interactable = true;
            _lastBtn.onClick.AddListener(OnLastClicked);

            SpawnHeroItems();
            SpawnLevelItems();
            RefreshHeroItems();
            RefreshLevelItems();
            RefreshPreview();

            var stageNum = GetNode<TMP_Text>("stageNum").transform as RectTransform;
            var roleInfo = GetNode<TMP_Text>("skillName").transform.parent as RectTransform;
            var useHeroBtn = GetNode<Button>("useHeroBtn").transform as RectTransform;
            var unlockHeroBtn = GetNode<Button>("unlockHeroBtn").transform as RectTransform;
            var startGameBtn = GetNode<Button>("StartGameBtn").transform as RectTransform;

            _transition = new LevelUITransition(new LevelUITransitionTargets
            {
                Root = transform as RectTransform,
                Hor = UI.GetGameObject("hor").transform as RectTransform,
                HeroCard = UI.GetGameObject("PlayerItem").transform as RectTransform,
                RoleInfo = roleInfo,
                StageInfo = UI.GetGameObject("stageInfo").transform as RectTransform,
                StageNum = stageNum,
                HeroPanel = UI.GetGameObject("heroSelect").transform as RectTransform,
                LevelPanel = UI.GetGameObject("levelSelect").transform as RectTransform,
                LastBtn = GetNode<Button>("LastBtn").transform as RectTransform,
                UseHeroBtn = useHeroBtn,
                UnlockHeroBtn = unlockHeroBtn,
                StartGameBtn = startGameBtn,
                HeroItems = CollectRects(_heroItems),
                LevelItems = CollectRects(_levelItems)
            });
            _transition.Prepare(
                ViewModel.ShowHeroSelect.Value,
                ViewModel.ShowStageInfo.Value,
                ViewModel.ShowLevelSelect.Value);

            // 面板显隐订阅必须排在下面的 RefreshHeroItems / RefreshLevelItems 订阅之前：
            // 先激活面板，SetSelected 才会走抬卡动画，而不是 inactive 时的吸附分支。
            Binding.Add(ViewModel.ShowHeroSelect.Subscribe(show =>
            {
                if (show)
                {
                    _suppressSelectionUntil = Time.unscaledTime + 0.25f;
                }

                _transition.SetHeroPanel(show, _entrancePlayed);
            }, emitCurrent: false));
            Binding.Add(ViewModel.ShowStageInfo.Subscribe(show =>
            {
                if (show)
                {
                    _suppressSelectionUntil = Time.unscaledTime + 0.35f;
                }

                _transition.SetStageInfo(show, _entrancePlayed);
            }, emitCurrent: false));
            Binding.Add(ViewModel.ShowLevelSelect.Subscribe(show =>
            {
                if (show)
                {
                    _suppressSelectionUntil = Time.unscaledTime + 0.35f;
                }

                _transition.SetLevelPanel(show, _entrancePlayed);
            }, emitCurrent: false));

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

            // 按钮显隐仍由 BindActive 控制，这里只补一个淡入，避免同位按钮硬切。
            Binding.Add(ViewModel.ShowUseHeroBtn.Subscribe(show =>
            {
                if (show && _entrancePlayed)
                {
                    _transition.PlayButtonIn(useHeroBtn);
                }
            }, emitCurrent: false));
            Binding.Add(ViewModel.ShowUnlockHeroBtn.Subscribe(show =>
            {
                if (show && _entrancePlayed)
                {
                    _transition.PlayButtonIn(unlockHeroBtn);
                }
            }, emitCurrent: false));
            Binding.Add(ViewModel.ShowStartGameBtn.Subscribe(show =>
            {
                if (show && _entrancePlayed)
                {
                    _transition.PlayButtonIn(startGameBtn);
                }
            }, emitCurrent: false));

            Binding.Add(ViewModel.SelectedHeroId.Subscribe(_ =>
            {
                RefreshHeroItems();
                RefreshPreview();
                var heroId = ViewModel.SelectedHeroId.Value;
                var changed = heroId != _lastAnimatedHeroId;
                _lastAnimatedHeroId = heroId;
                if (changed && _entrancePlayed && Time.unscaledTime >= _suppressSelectionUntil)
                {
                    _transition.PlayHeroChanged();
                }
            }, emitCurrent: false));
            Binding.Add(ViewModel.SelectedLevelId.Subscribe(_ =>
            {
                RefreshLevelItems();
                PlayStageChangedIfNeeded();
            }, emitCurrent: false));
            Binding.Add(ViewModel.SelectedDifficulty.Subscribe(_ =>
            {
                RefreshLevelItems();
                PlayStageChangedIfNeeded();
            }, emitCurrent: false));
        }

        protected override Task OnViewOpen()
        {
            RefreshHeroItems();
            RefreshPreview();
            return Task.CompletedTask;
        }

        protected override Task OnViewClose()
        {
            _inputUnblockAt = 0f;
            _closeAt = 0f;
            _closing = false;
            if (_lastBtn != null)
            {
                _lastBtn.onClick.RemoveListener(OnLastClicked);
                _lastBtn = null;
            }

            _transition?.Kill();
            _transition = null;
            _entrancePlayed = false;

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

        /// <summary>
        /// 上一页遮罩撤掉、本页真正露出后播入场；入场期间先挡住输入，播完再放开。
        /// </summary>
        public override void OnPresented()
        {
            base.OnPresented();
            if (ViewModel == null || _transition == null)
            {
                return;
            }

            _closing = false;
            _closeAt = 0f;
            _entrancePlayed = false;
            _lastAnimatedHeroId = ViewModel.SelectedHeroId.Value;
            _lastAnimatedLevelId = ViewModel.SelectedLevelId.Value;
            _lastAnimatedDifficulty = ViewModel.SelectedDifficulty.Value;
            _transition.PlayEntrance(ViewModel.Phase.Value == LevelUiPhase.Stage);
            _inputUnblockAt = Time.unscaledTime + _transition.EntranceDuration;
        }

        /// <summary>
        /// 入场计时用 LateUpdate 兜底：即使页面在入场途中被临时隐藏，
        /// 重新激活后也会在第一帧把输入放开，不会永久锁住界面。
        /// 退场同理：到点再执行真正的关闭命令，不依赖 Tween 的 OnComplete。
        /// </summary>
        private void LateUpdate()
        {
            if (_closeAt > 0f && Time.unscaledTime >= _closeAt)
            {
                _closeAt = 0f;
                var vm = ViewModel;
                if (vm != null)
                {
                    vm.LastBtnCommand.Execute();
                }

                return;
            }

            if (_inputUnblockAt <= 0f || Time.unscaledTime < _inputUnblockAt)
            {
                return;
            }

            _inputUnblockAt = 0f;
            _entrancePlayed = true;
            _transition?.SetInputBlocked(false);
        }

        /// <summary>
        /// Hero 阶段：先播退场，延时再执行 LastBtnCommand（它会 Close 当前页）。
        /// Stage 阶段：保持原逻辑，立即返回选角。
        /// </summary>
        private void OnLastClicked()
        {
            if (ViewModel == null || _closing)
            {
                return;
            }

            if (ViewModel.Phase.Value == LevelUiPhase.Stage)
            {
                ViewModel.LastBtnCommand.Execute();
                return;
            }

            if (_transition == null)
            {
                ViewModel.LastBtnCommand.Execute();
                return;
            }

            _closing = true;
            _inputUnblockAt = 0f;
            _transition.PlayExit();
            _closeAt = Time.unscaledTime + _transition.ExitDuration + 0.05f;
        }

        private void PlayStageChangedIfNeeded()
        {
            var levelId = ViewModel.SelectedLevelId.Value;
            var difficulty = ViewModel.SelectedDifficulty.Value;
            var changed = levelId != _lastAnimatedLevelId || difficulty != _lastAnimatedDifficulty;
            _lastAnimatedLevelId = levelId;
            _lastAnimatedDifficulty = difficulty;
            if (!changed || !_entrancePlayed || Time.unscaledTime < _suppressSelectionUntil)
            {
                return;
            }

            _transition?.PlayStageChanged();
            _suppressSelectionUntil = Time.unscaledTime + 0.18f;
        }

        private static List<RectTransform> CollectRects<T>(List<T> items) where T : Component
        {
            var rects = new List<RectTransform>(items.Count);
            for (var i = 0; i < items.Count; i++)
            {
                rects.Add(items[i] != null ? items[i].transform as RectTransform : null);
            }

            return rects;
        }

        private T GetNode<T>(string key) where T : Component
        {
            return UI.GetGameObject(key).GetComponent<T>();
        }

        /// <summary>
        /// 技能描述区自适应：顶边保持预制体位置不动，底边向下撑到「难度X 关卡Y」大标签上方，
        /// 再打开 TMP 自动缩放。描述短时仍是原字号（36），描述长时按可用高度等比缩字，
        /// 不会再溢出框外压到大标签上。重复调用结果一致（高度只增不减、顶边不动）。
        /// </summary>
        private void FitSkillInfoArea()
        {
            var info = GetNode<TMP_Text>("skillinfo");
            var rect = info != null ? info.rectTransform : null;
            var parent = rect != null ? rect.parent as RectTransform : null;
            if (parent == null)
            {
                return;
            }

            var size = rect.sizeDelta;
            var height = size.y;
            // 文本区顶边（相对锚点）：锚点居中，anchoredPosition 即框中心
            var top = rect.anchoredPosition.y + height * (1f - rect.pivot.y);

            var stageLabel = GetNode<TMP_Text>("stageNum");
            if (stageLabel != null)
            {
                // 难度标签的顶边按世界坐标换算到文本区父级，再折算成相对锚点（父级 rect 中心）的距离
                var labelRect = stageLabel.rectTransform;
                var labelTopWorld = labelRect.TransformPoint(new Vector3(0f, labelRect.rect.yMax, 0f));
                var anchorFromParentCenter = (0.5f - parent.pivot.y) * parent.rect.height;
                var labelTop = parent.InverseTransformPoint(labelTopWorld).y - anchorFromParentCenter;
                height = Mathf.Max(height, top - labelTop - SkillInfoToStageLabelGap);
            }

            if (!Mathf.Approximately(height, size.y))
            {
                size.y = height;
                rect.sizeDelta = size;
            }

            var position = rect.anchoredPosition;
            position.y = top - height * (1f - rect.pivot.y);
            rect.anchoredPosition = position;

            info.enableAutoSizing = true;
            info.fontSizeMax = SkillInfoMaxFontSize;
            info.fontSizeMin = SkillInfoMinFontSize;
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
                var clickedStage = stage;
                item.Clicked += _ =>
                {
                    var unlocked = ViewModel.IsLevelUnlocked(clickedStage);
                    ViewModel.SelectLevel(clickedStage.Id);
                    if (!unlocked)
                    {
                        _transition?.PlayLockedShake();
                    }
                };
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
                // 难度按钮上显示"当前选中英雄"对该难度的通关状态（已通关则把固定的「难度」小字换掉）。
                item.SetClearStatus(ViewModel.IsHeroDifficultyCleared(ViewModel.SelectedHeroId.Value, stage.Difficulty));
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

            var hero = CardShare.Contracts.Config.HeroConfig.Get(ViewModel.SelectedHeroId.Value);
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

            // 关卡人物预览不显示攻击块，解锁态刷新后统一隐藏。
            _playerItem.HideAttackStat();
        }
    }
}

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using App.Bootstrap;
using App.Config;
using CardShare.Contracts;
using CardShare.Contracts.Config;
using App.Game;
using App.Guide;
using App.Item;
using App.Resources;
using DG.Tweening;
using Framework.UI.Binding;
using Framework.UI.Navigation;
using Framework.UI.View;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace App.UI.Popup
{
    /// <summary>
    /// 通关商店弹窗。sellItem / mineItem 是隐藏模板，克隆到 sellHor 与 MineHor。
    /// 预制体上有两套头部：PVEShop（标题/刷新/倒计时）与 PVPShop（另一套头部 + horBtns2 三个技能补充按钮），
    /// 运行时按 <see cref="GameSession.IsPvp"/> 二选一激活，PVPShop 专属技能按钮只在 PVP 下绑定。
    /// </summary>
    [AutoScreen(AppScreenIds.BattleShopPop, UILayer.Popup, ResResourcePaths.BattleShopPop)]
    public sealed class BattleShopPopView : ViewBase<BattleShopPopViewModel>
    {
        /// <summary>PVPShop/horBtns2 下的一个技能补充按钮。</summary>
        private sealed class PvpSkillButton
        {
            public Button Button;
            public TMP_Text Label;
            public TMP_Text Price;
            public int SkillId;
            public Color PriceColor;
            public UnityEngine.Events.UnityAction Handler;
        }

        private readonly List<ShopItem> _sellItems = new List<ShopItem>();
        private readonly List<ShopItem> _mineItems = new List<ShopItem>();
        private readonly List<int> _mineRelicIds = new List<int>();
        private readonly List<int> _mineLevels = new List<int>();
        private readonly List<int> _mineCounts = new List<int>();
        private readonly List<string> _guideTargetIds = new List<string>(4);
        private readonly List<RectTransform> _guideHoleRects = new List<RectTransform>(16);
        private readonly List<PvpSkillButton> _pvpSkillButtons = new List<PvpSkillButton>(3);
        private GuideTargetRegistry _guideTargets;
        private GameObject _sellTemplate;
        private GameObject _mineTemplate;
        private Transform _mineContent;
        private Transform _pveShop;
        private Transform _pvpShop;
        private int _pendingMineRevealId;
        private CanvasGroup _pendingMineGroup;
        private int _lastShownCount = -1;

        public static BattleShopPopView FindOpen()
        {
            return FindObjectOfType<BattleShopPopView>();
        }

        public void HoldIncomingMine(int relicId)
        {
            _pendingMineRevealId = relicId > 0 ? relicId : 0;
        }

        public void ClearIncomingMine()
        {
            _pendingMineRevealId = 0;
            _pendingMineGroup = null;
        }

        public RectTransform GetIncomingMineRect(int relicId)
        {
            RebuildMineLayout();
            var item = FindMineItem(relicId);
            if (item == null)
            {
                return null;
            }

            var card = item.GetComponentInChildren<ItemCard>(true);
            return card != null ? card.transform as RectTransform : item.transform as RectTransform;
        }

        public void RevealIncomingMine(int relicId, bool punch = true)
        {
            var item = FindMineItem(relicId > 0 ? relicId : _pendingMineRevealId);
            _pendingMineRevealId = 0;
            if (_pendingMineGroup != null)
            {
                _pendingMineGroup.alpha = 1f;
                _pendingMineGroup.blocksRaycasts = true;
                _pendingMineGroup = null;
            }
            else if (item != null)
            {
                var cg = GetSlotCanvasGroup(item);
                if (cg != null)
                {
                    cg.alpha = 1f;
                    cg.blocksRaycasts = true;
                }
            }

            if (punch && item != null)
            {
                PunchSlot(item);
            }

            // 飞入落定后再登记已购挖孔，供「点击刚买的圣物」步使用。
            RegisterBoughtMineGuideTarget();
        }

        protected override void OnBind()
        {
            ApplyShopVariant();
            BindShopHeader();
            Binding.BindCommand(GetNode<Button>("NextStageBtn"), ViewModel.NextStageCommand);
            Binding.BindCommand(GetNode<Button>("ReturnToHomeBtn"), ViewModel.ReturnToHomeCommand);
            Binding.BindText(GetNode<TMP_Text>("MaxNum"), ViewModel.CarryNum);
            BindPvpSkillButtons();

            EnsureSellItems();
            EnsureMineItems();
            RegisterGuideTargets();
            Binding.Add(ViewModel.ShopRevision.Subscribe(_ => RefreshItems(), emitCurrent: true));
        }

        /// <summary>
        /// 两套头部二选一：PVE 用 PVEShop，PVP 用 PVPShop（含技能补充按钮）。
        /// 老预制体只有单一 BG 时保持不动，回落到按名查找。
        /// </summary>
        private void ApplyShopVariant()
        {
            _pveShop = FindDeep(transform, "PVEShop");
            _pvpShop = FindDeep(transform, "PVPShop");
            var usePvp = ViewModel != null && ViewModel.ShowPvpSkills && _pvpShop != null;
            if (_pveShop != null)
            {
                _pveShop.gameObject.SetActive(!usePvp);
            }

            if (_pvpShop != null)
            {
                _pvpShop.gameObject.SetActive(usePvp);
            }
        }

        /// <summary>当前模式实际显示的头部根节点。</summary>
        private Transform ActiveShopRoot()
        {
            if (ViewModel != null && ViewModel.ShowPvpSkills && _pvpShop != null)
            {
                return _pvpShop;
            }

            return _pveShop != null ? _pveShop : transform;
        }

        /// <summary>按名在激活的头部里找节点。两套变体同名节点（RefreshBtn/timeText 等）靠这里区分，别走 UIReference。</summary>
        private T FindHeader<T>(string nodeName) where T : Component
        {
            var root = ActiveShopRoot();
            var found = FindDeep(root, nodeName);
            if (found == null && root != transform)
            {
                found = FindDeep(transform, nodeName);
            }

            return found != null ? found.GetComponent<T>() : null;
        }

        private void BindShopHeader()
        {
            var refreshGold = FindHeader<TMP_Text>("RefreshGooldNum");
            if (refreshGold != null)
            {
                Binding.BindText(refreshGold, ViewModel.RefreshGoldNum);
            }

            var refreshNum = FindHeader<TMP_Text>("refreshNum");
            if (refreshNum != null)
            {
                Binding.BindText(refreshNum, ViewModel.RefreshNum);
                Binding.BindActive(refreshNum.gameObject, ViewModel.ShowRefreshNum);
            }

            var refreshBtn = FindHeader<Button>("RefreshBtn");
            if (refreshBtn != null)
            {
                Binding.BindCommand(refreshBtn, ViewModel.RefreshCommand);
            }

            BindShopTime(FindHeader<TMP_Text>("timeText"));
        }

        /// <summary>PVP 商店专属：horBtns2 三个按钮对应搓牌/替换/透视，点击花金币买 1 次（下一轮生效）。</summary>
        private void BindPvpSkillButtons()
        {
            for (var i = 0; i < _pvpSkillButtons.Count; i++)
            {
                var entry = _pvpSkillButtons[i];
                if (entry.Button != null && entry.Handler != null)
                {
                    entry.Button.onClick.RemoveListener(entry.Handler);
                }
            }

            _pvpSkillButtons.Clear();
            if (_pvpShop == null || ViewModel == null || !ViewModel.ShowPvpSkills)
            {
                return;
            }

            // 节点名沿用 GameUI 的技能按钮：PeekGood=搓牌、TiHuanGood=替换、ChaKanGood=透视。
            AddPvpSkillButton("PeekGood", PvpSkillIds.Rub);
            AddPvpSkillButton("TiHuanGood", PvpSkillIds.Replace);
            AddPvpSkillButton("ChaKanGood", PvpSkillIds.Peek);
        }

        private void AddPvpSkillButton(string nodeName, int skillId)
        {
            var root = FindDeep(_pvpShop, nodeName);
            if (root == null)
            {
                return;
            }

            var button = root.GetComponent<Button>();
            if (button == null)
            {
                button = root.GetComponentInChildren<Button>(true);
            }

            if (button == null)
            {
                return;
            }

            var entry = new PvpSkillButton
            {
                Button = button,
                Label = FindButtonLabel(root),
                Price = FindButtonPrice(root),
                SkillId = skillId
            };
            if (entry.Price != null)
            {
                entry.PriceColor = entry.Price.color;
            }

            entry.Handler = () => OnPvpSkillClicked(entry.SkillId);
            button.onClick.AddListener(entry.Handler);
            _pvpSkillButtons.Add(entry);
        }

        /// <summary>技能按钮文案：「搓牌(3/3)」这种节点名是 Text (TMP)，改名时回落到直属子节点的第一个文本。</summary>
        private static TMP_Text FindButtonLabel(Transform buttonRoot)
        {
            var named = FindDeep(buttonRoot, "Text (TMP)");
            if (named != null)
            {
                return named.GetComponent<TMP_Text>();
            }

            for (var i = 0; i < buttonRoot.childCount; i++)
            {
                var text = buttonRoot.GetChild(i).GetComponent<TMP_Text>();
                if (text != null)
                {
                    return text;
                }
            }

            return null;
        }

        /// <summary>价格文本在 PriceBar/Num（Coin 是图标，没有文本）。</summary>
        private static TMP_Text FindButtonPrice(Transform buttonRoot)
        {
            var num = FindDeep(buttonRoot, "Num");
            return num != null ? num.GetComponent<TMP_Text>() : null;
        }

        private void OnPvpSkillClicked(int skillId)
        {
            ViewModel?.BuyPvpSkill(skillId);
        }

        /// <summary>每帧刷新入口都在 ShopRevision 上：价格/下一轮上限随快照变化。</summary>
        private void RefreshPvpSkills()
        {
            for (var i = 0; i < _pvpSkillButtons.Count; i++)
            {
                var entry = _pvpSkillButtons[i];
                var price = ViewModel.PvpSkillPrice(entry.SkillId);
                if (entry.Label != null)
                {
                    entry.Label.text = ViewModel.FormatPvpSkillText(entry.SkillId);
                }

                if (entry.Price != null)
                {
                    entry.Price.text = price > 0 ? price.ToString() : string.Empty;
                    entry.Price.color = ViewModel.CanBuyPvpSkill(entry.SkillId)
                        ? entry.PriceColor
                        : ThemeColors.ShopPriceUnaffordable;
                }

                if (entry.Button != null)
                {
                    // 买不起也保持可点：点了走 Toast「金币不足」，与买槽格一致（不置灰）。
                    entry.Button.interactable = price > 0;
                }
            }
        }

        private void RegisterGuideTargets()
        {
            UnregisterGuideTargets();
            if (!AppServices.IsReady)
            {
                return;
            }

            _guideTargets = AppServices.Resolve<GuideTargetRegistry>();
            if (_guideTargets == null)
            {
                return;
            }

            // 货架挖孔：每张卡 + 下方价格，避免遮罩盖住金币价。
            _guideHoleRects.Clear();
            for (var i = 0; i < _sellItems.Count; i++)
            {
                var item = _sellItems[i];
                if (item == null || !item.gameObject.activeInHierarchy || item.RelicConfigId <= 0)
                {
                    continue;
                }

                item.CollectGuideHoleRects(_guideHoleRects);
            }

            if (_guideHoleRects.Count > 0)
            {
                _guideTargets.RegisterUiGroup(GuideTargetIds.ShopSellOffers, _guideHoleRects);
                _guideTargetIds.Add(GuideTargetIds.ShopSellOffers);
            }

            RegisterBoughtMineGuideTarget();
        }

        private void RegisterBoughtMineGuideTarget()
        {
            if (_guideTargets == null)
            {
                return;
            }

            var relicId = GuideSignals.LastBoughtShopRelicId;
            if (relicId <= 0)
            {
                return;
            }

            var item = FindMineItem(relicId);
            if (item == null)
            {
                return;
            }

            _guideHoleRects.Clear();
            item.CollectGuideHoleRects(_guideHoleRects);
            if (_guideHoleRects.Count == 0)
            {
                return;
            }

            _guideTargets.RegisterUiGroup(GuideTargetIds.ShopBoughtMine, _guideHoleRects);
            if (!_guideTargetIds.Contains(GuideTargetIds.ShopBoughtMine))
            {
                _guideTargetIds.Add(GuideTargetIds.ShopBoughtMine);
            }
        }

        private void UnregisterGuideTargets()
        {
            if (_guideTargets != null && _guideTargetIds.Count > 0)
            {
                _guideTargets.UnregisterAll(_guideTargetIds);
            }

            _guideTargetIds.Clear();
        }

        private void BindShopTime(TMP_Text time)
        {
            var table = ViewModel.Table;
            if (time == null)
            {
                return;
            }

            if (table == null)
            {
                time.gameObject.SetActive(false);
                return;
            }

            Binding.BindText(time, table.TimeValue);
            Binding.BindActive(time.gameObject, table.ShowTime);
        }

        protected override Task OnViewClose()
        {
            UnregisterGuideTargets();
            if (AppServices.IsReady)
            {
                var guide = AppServices.Resolve<IGuideService>();
                if (guide != null &&
                    guide.IsRunning &&
                    guide.CurrentGroupId == GuideGroupIds.FirstShop)
                {
                    guide.Abort();
                }
            }

            // 关闭时只做显隐清理，不启动 punch：对象即将销毁，独立 Update 的 Tween 会访问已销毁 RectTransform。
            RevealIncomingMine(_pendingMineRevealId, punch: false);
            return Task.CompletedTask;
        }

        private static Transform FindDeep(Transform root, string name)
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
                var found = FindDeep(root.GetChild(i), name);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private T GetNode<T>(string key) where T : Component
        {
            return UI.GetGameObject(key).GetComponent<T>();
        }

        private void EnsureSellItems()
        {
            _sellTemplate = UI.GetGameObject("sellItem");
            _sellTemplate.SetActive(false);
            var root = UI.GetGameObject("sellHor").transform;
            _sellItems.Clear();
            for (var i = 0; i < GameBalance.ShopOfferCount; i++)
            {
                _sellItems.Add(CloneItem(_sellTemplate, root, $"sellItem_{i}", OnSellClicked));
            }
        }

        private void EnsureMineItems()
        {
            _mineTemplate = UI.GetGameObject("mineItem");
            _mineTemplate.SetActive(false);
            var mineHor = UI.GetGameObject("MineHor").transform;
            _mineContent = FindMineContent(mineHor);
        }

        private static Transform FindMineContent(Transform mineHor)
        {
            var scroll = mineHor.GetComponent<ScrollRect>();
            if (scroll != null && scroll.content != null)
            {
                return scroll.content;
            }

            var named = FindDeep(mineHor, "Content");
            return named != null ? named : mineHor;
        }

        private ShopItem CloneItem(
            GameObject template,
            Transform parent,
            string name,
            Action<ShopItem> onClick,
            bool wrapForGrid = false)
        {
            var host = parent;
            if (wrapForGrid)
            {
                var slot = new GameObject(name, typeof(RectTransform));
                slot.layer = parent.gameObject.layer;
                var rt = slot.GetComponent<RectTransform>();
                rt.SetParent(parent, false);
                rt.localScale = Vector3.one;
                host = rt;
            }

            var go = UnityEngine.Object.Instantiate(template, host, false);
            go.name = wrapForGrid ? template.name : name;
            go.SetActive(false);
            var binds = go.GetComponentsInChildren<UIBind>(true);
            for (var i = 0; i < binds.Length; i++)
            {
                UnityEngine.Object.Destroy(binds[i]);
            }

            var item = go.GetComponentInChildren<ShopItem>(true);
            item.BindClick(onClick);
            return item;
        }

        private void OnSellClicked(ShopItem item)
        {
            if (item == null || item.RelicConfigId <= 0)
            {
                return;
            }

            GuideSignals.NotifyShopOfferClicked();
            _ = ViewModel.OpenDetail(item.RelicConfigId, buying: true);
        }

        private void OnMineClicked(ShopItem item)
        {
            if (item == null)
            {
                return;
            }

            if (item.State == ShopItemState.BuySlot)
            {
                ViewModel.AddNewCommand.Execute();
                return;
            }

            if (item.RelicConfigId <= 0)
            {
                // Empty 态与无数据格不可交互。
                return;
            }

            if (GuideSignals.LastBoughtShopRelicId > 0 &&
                item.RelicConfigId == GuideSignals.LastBoughtShopRelicId)
            {
                GuideSignals.NotifyShopMineClicked();
            }

            _ = ViewModel.OpenDetail(item.RelicConfigId, buying: false);
        }

        private void RefreshItems()
        {
            RefreshSellItems();
            RefreshMineItems();
            RefreshPvpSkills();
            // 货架布局后再登记，避免 sellHor 尚未撑开导致挖孔失败、引导像没生效。
            RegisterGuideTargets();
        }

        private void RefreshSellItems()
        {
            var offers = ViewModel.Session.Run.ShopOfferIds;
            for (var i = 0; i < _sellItems.Count; i++)
            {
                var item = _sellItems[i];
                if (i >= offers.Count)
                {
                    SetSlotActive(item, false);
                    continue;
                }

                var relic = RelicConfig.Get(offers[i]);
                if (relic == null)
                {
                    SetSlotActive(item, false);
                    continue;
                }

                SetSlotActive(item, true);
                var price = ViewModel.Session.EffectiveBuyPrice(relic.Id);
                item.Bind(
                    relic,
                    ViewModel.GetRelicIcon(relic),
                    buyPrice: price,
                    affordable: RelicMechanics.CanAfford(ViewModel.Session.Run, price),
                    level: 1,
                    count: ViewModel.Session.RelicOwnedCount(relic.Id));
            }
        }

        /// <summary>
        /// MineHor 拼格：[聚合圣物格 …][空槽格 × (CarryMax − 已持份数)][买槽格 × 1（还有可解锁槽时）]。
        /// 聚合段在前，飞入动画的索引语义（LastMineIndex / FindMineItem）不受后段影响。
        /// </summary>
        private void RefreshMineItems()
        {
            if (_mineTemplate == null || _mineContent == null)
            {
                return;
            }

            var session = ViewModel.Session;
            CollectMineRelics(session.Run.RelicConfigIds);
            var emptySlots = Math.Max(0, session.RelicCarryMax - session.Run.RelicConfigIds.Count);
            var showBuySlot = ViewModel.ShowAddNew.Value;
            var shown = _mineRelicIds.Count + emptySlots + (showBuySlot ? 1 : 0);
            while (_mineItems.Count < shown)
            {
                _mineItems.Add(CloneItem(_mineTemplate, _mineContent, $"mineItem_{_mineItems.Count}", OnMineClicked, wrapForGrid: true));
            }

            for (var i = 0; i < _mineItems.Count; i++)
            {
                var item = _mineItems[i];
                if (i >= shown)
                {
                    SetSlotActive(item, false, _mineContent);
                    continue;
                }

                SetSlotActive(item, true, _mineContent);
                if (i < _mineRelicIds.Count)
                {
                    BindMineRelic(item, i);
                }
                else if (showBuySlot && i == shown - 1)
                {
                    BindBuySlot(item);
                }
                else
                {
                    item.SetState(ShopItemState.Empty);
                }
            }

            RebuildMineLayout();
            if (_pendingMineGroup != null)
            {
                // 买入圣物：滚到待揭示格可见（可见则视口不动）。
                ScrollMineToRect(_pendingMineGroup.transform as RectTransform);
            }
            else if (_lastShownCount >= 0 && shown > _lastShownCount)
            {
                // 解锁槽位会让格子总数 +1（新槽在底部），滚到底展示；首帧与买入（总数不变）不滚。
                ScrollMineToBottom();
            }

            _lastShownCount = shown;
        }

        private void BindMineRelic(ShopItem item, int i)
        {
            var relicId = _mineRelicIds[i];
            var relic = RelicConfig.Get(relicId);
            if (relic == null)
            {
                SetSlotActive(item, false, _mineContent);
                return;
            }

            item.Bind(
                relic,
                ViewModel.GetRelicIcon(relic),
                forSale: false,
                level: _mineLevels[i],
                count: _mineCounts[i]);
            ApplyIncomingMineVisibility(item, i == LastMineIndex(relicId) && relicId == _pendingMineRevealId);
        }

        private void BindBuySlot(ShopItem item)
        {
            var session = ViewModel.Session;
            var showsPrice = ViewModel.BuySlotShowsPrice;
            var price = showsPrice ? session.PvpNextRelicSlotCost : 0;
            item.SetState(ShopItemState.BuySlot);
            item.SetBuySlotContent(price, affordable: !showsPrice || RelicMechanics.CanAfford(session.Run, price), showPrice: showsPrice);
        }

        private void CollectMineRelics(IReadOnlyList<int> owned)
        {
            _mineRelicIds.Clear();
            _mineLevels.Clear();
            _mineCounts.Clear();
            if (owned == null)
            {
                return;
            }

            for (var i = 0; i < owned.Count; i++)
            {
                var relicId = owned[i];
                if (relicId <= 0)
                {
                    continue;
                }

                var level = ViewModel.Session.RelicLevelAt(i);
                var slot = -1;
                for (var n = 0; n < _mineRelicIds.Count; n++)
                {
                    if (_mineRelicIds[n] == relicId && _mineLevels[n] == level)
                    {
                        slot = n;
                        break;
                    }
                }

                if (slot >= 0)
                {
                    _mineCounts[slot]++;
                    continue;
                }

                _mineRelicIds.Add(relicId);
                _mineLevels.Add(level);
                _mineCounts.Add(1);
            }
        }

        private int LastMineIndex(int relicId)
        {
            for (var i = _mineRelicIds.Count - 1; i >= 0; i--)
            {
                if (_mineRelicIds[i] == relicId)
                {
                    return i;
                }
            }

            return -1;
        }

        private void ApplyIncomingMineVisibility(ShopItem item, bool hideIncoming)
        {
            var cg = GetOrAddSlotCanvasGroup(item);
            if (cg == null)
            {
                return;
            }

            var hide = hideIncoming && _pendingMineRevealId > 0 && item.RelicConfigId == _pendingMineRevealId;
            cg.alpha = hide ? 0f : 1f;
            cg.blocksRaycasts = !hide;
            if (!hide)
            {
                return;
            }

            // 只记录待揭示格；滚动挪到 RefreshMineItems 末尾布局完成后做，
            // 且不再滚底——槽位视图下买入落点在聚合段，滚底会把落点带出视口。
            _pendingMineGroup = cg;
        }

        private ShopItem FindMineItem(int relicId)
        {
            if (relicId <= 0)
            {
                return null;
            }

            ShopItem found = null;
            for (var i = 0; i < _mineItems.Count; i++)
            {
                var item = _mineItems[i];
                if (item != null && item.RelicConfigId == relicId && item.gameObject.activeInHierarchy)
                {
                    found = item;
                }
            }

            return found;
        }

        private void RebuildMineLayout()
        {
            var content = _mineContent as RectTransform;
            if (content != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(content);
            }

            Canvas.ForceUpdateCanvases();
        }

        private void ScrollMineToBottom()
        {
            RebuildMineLayout();
            var mineHor = UI.GetGameObject("MineHor");
            var scroll = mineHor != null ? mineHor.GetComponent<ScrollRect>() : null;
            if (scroll == null)
            {
                return;
            }

            scroll.verticalNormalizedPosition = 0f;
        }

        /// <summary>纵向滚动 MineHor 让目标槽在视口内可见；已可见不动，避免视口跳动。</summary>
        private void ScrollMineToRect(RectTransform slot)
        {
            if (slot == null)
            {
                return;
            }

            var mineHor = UI.GetGameObject("MineHor");
            var scroll = mineHor != null ? mineHor.GetComponent<ScrollRect>() : null;
            if (scroll == null || scroll.viewport == null || scroll.content == null)
            {
                return;
            }

            var content = scroll.content;
            var viewport = scroll.viewport;
            var scrollable = content.rect.height - viewport.rect.height;
            if (scrollable <= 0f)
            {
                return;
            }

            // 统一换算成"距 content 顶部"的一维坐标（UGUI y 向上，越往下值越大）。
            var contentTop = content.TransformPoint(0f, content.rect.yMax, 0f).y;
            var viewTop = viewport.TransformPoint(0f, viewport.rect.yMax, 0f).y;
            var viewHeight = viewport.rect.height;
            var slotTop = contentTop - slot.TransformPoint(0f, slot.rect.yMax, 0f).y;
            var slotBottom = contentTop - slot.TransformPoint(0f, slot.rect.yMin, 0f).y;
            var viewAt = contentTop - viewTop;

            const float pad = 20f;
            float target;
            if (slotBottom < viewAt + pad)
            {
                target = slotBottom - pad;
            }
            else if (slotTop > viewAt + viewHeight - pad)
            {
                target = slotTop - viewHeight + pad;
            }
            else
            {
                return;
            }

            target = Mathf.Clamp(target, 0f, scrollable);
            scroll.verticalNormalizedPosition = 1f - target / scrollable;
        }

        private Transform GetSlotRoot(ShopItem item)
        {
            if (item == null)
            {
                return null;
            }

            var t = item.transform;
            while (t.parent != null && t.parent != _mineContent)
            {
                t = t.parent;
            }

            return t;
        }

        private CanvasGroup GetSlotCanvasGroup(ShopItem item)
        {
            var slot = GetSlotRoot(item);
            return slot != null ? slot.GetComponent<CanvasGroup>() : null;
        }

        private CanvasGroup GetOrAddSlotCanvasGroup(ShopItem item)
        {
            var slot = GetSlotRoot(item);
            if (slot == null)
            {
                return null;
            }

            var cg = slot.GetComponent<CanvasGroup>();
            return cg != null ? cg : slot.gameObject.AddComponent<CanvasGroup>();
        }

        private static void PunchSlot(ShopItem item)
        {
            var rt = item.transform as RectTransform;
            if (rt == null)
            {
                return;
            }

            rt.DOKill();
            rt.DOPunchScale(Vector3.one * 0.08f, 0.28f, 6, 0.6f)
                .SetUpdate(true)
                .SetLink(rt.gameObject, LinkBehaviour.KillOnDestroy);
        }

        private static void SetSlotActive(ShopItem item, bool active, Transform layoutRoot = null)
        {
            if (item == null)
            {
                return;
            }

            var t = item.transform;
            while (t != null && t != layoutRoot)
            {
                t.gameObject.SetActive(active);
                var parent = t.parent;
                if (parent == null || parent == layoutRoot)
                {
                    if (!active)
                    {
                        var cg = t.GetComponent<CanvasGroup>();
                        if (cg != null)
                        {
                            cg.alpha = 1f;
                            cg.blocksRaycasts = true;
                        }
                    }

                    break;
                }

                t = parent;
                if (layoutRoot == null)
                {
                    t.gameObject.SetActive(active);
                    break;
                }
            }
        }
    }
}

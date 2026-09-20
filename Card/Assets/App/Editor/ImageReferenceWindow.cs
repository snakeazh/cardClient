using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace App.Editor
{
    /// <summary>
    /// 图片引用查看器：扫描 Assets 下所有图片，列出引用它们的资源
    /// （预制体/场景/材质/图集等；统计的是递归依赖，预制体经由材质用到贴图也会记到预制体头上）。
    /// 支持按名称搜索、限定文件夹范围（含子目录）、按引用数排序、筛选未被引用的图片。
    /// 注意：代码里按资源 key 字符串加载的图片、Resources 目录不计入引用，未被引用不代表可删除。
    /// 菜单：Tools/图片引用查看器
    /// </summary>
    public class ImageReferenceWindow : EditorWindow
    {
        private const string MenuPath = "Tools/图片引用查看器";
        private const string UnrefHint = "未被引用 ≠ 可删除：代码按 key 加载的图片、Resources 目录不计入统计";

        // 参与统计的图片扩展名
        private static readonly HashSet<string> ImageExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".png", ".jpg", ".jpeg", ".tga", ".psd", ".tif", ".tiff", ".bmp", ".exr", ".iff", ".pict"
        };

        // 作为引用者参与扫描的资源扩展名
        private static readonly HashSet<string> ReferencerExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".prefab", ".unity", ".mat", ".asset", ".spriteatlas", ".controller",
            ".overridecontroller", ".playable", ".mask", ".fontsettings"
        };

        private enum RefFilter { All, Referenced, Unreferenced }
        private enum SortMode { Name, RefCount }

        private class ImageInfo
        {
            public string Path;
            public string Name;
            public readonly List<string> Referencers = new List<string>();
            public Texture Icon;
        }

        // 左侧图片行模板
        private class ImageRow : VisualElement
        {
            public readonly Image Icon;
            public readonly Label Name;
            public readonly Label Count;

            public ImageRow()
            {
                style.flexDirection = FlexDirection.Row;
                style.height = 26;
                style.alignItems = Align.Center;
                Icon = new Image { style = { width = 20, height = 20, marginRight = 6 } };
                Name = new Label();
                Name.style.flexGrow = 1;
                Name.style.overflow = Overflow.Hidden;
                Name.style.textOverflow = TextOverflow.Ellipsis;
                Name.style.whiteSpace = WhiteSpace.NoWrap;
                Count = new Label();
                Count.style.fontSize = 10;
                Count.style.width = 72;
                Count.style.marginRight = 4;
                Count.style.unityTextAlign = TextAnchor.MiddleRight;
                Add(Icon);
                Add(Name);
                Add(Count);
            }
        }

        // 右侧引用者行模板
        private class RefRow : VisualElement
        {
            public readonly Image Icon;
            public readonly Label Name;
            public readonly Label Type;
            public readonly Label Path;

            public RefRow()
            {
                style.flexDirection = FlexDirection.Row;
                style.height = 26;
                style.alignItems = Align.Center;
                Icon = new Image { style = { width = 20, height = 20, marginRight = 6 } };
                Name = new Label();
                Name.style.fontSize = 12;
                Name.style.marginRight = 6;
                Type = new Label();
                Type.style.fontSize = 10;
                Type.style.color = new Color(0.45f, 0.65f, 0.9f);
                Type.style.marginRight = 6;
                Path = new Label();
                Path.style.flexGrow = 1;
                Path.style.fontSize = 10;
                Path.style.color = new Color(0.6f, 0.6f, 0.6f);
                Path.style.overflow = Overflow.Hidden;
                Path.style.textOverflow = TextOverflow.Ellipsis;
                Path.style.whiteSpace = WhiteSpace.NoWrap;
                Add(Icon);
                Add(Name);
                Add(Type);
                Add(Path);
            }
        }

        private List<ImageInfo> _all;
        private List<ImageInfo> _view = new List<ImageInfo>();
        private readonly Dictionary<string, string> _typeCache = new Dictionary<string, string>();
        private float _scanSeconds;

        [SerializeField] private DefaultAsset _folderAsset;
        private string _folderFilter = "";

        private RefFilter _filter = RefFilter.All;
        private SortMode _sortMode = SortMode.Name;
        private string _search = "";
        private bool _syncing;
        private ImageInfo _currentDetail;
        private Texture2D _detailPreview;
        private bool _previewIsRuntime;

        private ListView _imageList;
        private Label _statusLabel;
        private Label _emptyHint;
        private VisualElement _detailRoot;
        private Image _detailIcon;
        private Label _detailName;
        private Label _detailPath;
        private Label _detailCount;
        private ListView _refList;
        private ToolbarToggle _sortNameToggle;
        private ToolbarToggle _sortCountToggle;
        private ToolbarToggle _filterAllToggle;
        private ToolbarToggle _filterRefToggle;
        private ToolbarToggle _filterUnrefToggle;

        [MenuItem(MenuPath)]
        public static void Open()
        {
            var win = GetWindow<ImageReferenceWindow>();
            win.titleContent = new GUIContent("图片引用");
            win.minSize = new Vector2(760, 420);
        }

        private void CreateGUI()
        {
            var root = rootVisualElement;
            _folderFilter = _folderAsset != null ? AssetDatabase.GetAssetPath(_folderAsset) : "";

            // ── 顶部工具栏 ──
            var toolbar = new Toolbar();

            // 文件夹范围限定（含子目录）；清空后回到全项目。引用者仍全项目扫描，只影响左侧显示范围
            var folderField = new ObjectField
            {
                objectType = typeof(DefaultAsset),
                value = _folderAsset,
                allowSceneObjects = false,
                tooltip = "限定显示该文件夹（含子目录）下的图片，清空则显示全部"
            };
            folderField.label = "";
            folderField.style.width = 300;
            folderField.style.marginRight = 8;
            folderField.RegisterValueChangedCallback(e =>
            {
                _folderAsset = e.newValue as DefaultAsset;
                _folderFilter = _folderAsset != null ? AssetDatabase.GetAssetPath(_folderAsset) : "";
                ApplyFilter();
            });
            toolbar.Add(folderField);

            var search = new ToolbarSearchField { value = _search };
            search.style.width = 220;
            search.RegisterValueChangedCallback(e =>
            {
                _search = e.newValue;
                ApplyFilter();
            });
            toolbar.Add(search);
            toolbar.Add(new ToolbarSpacer());

            _filterAllToggle = new ToolbarToggle { text = "全部", value = true };
            _filterAllToggle.RegisterValueChangedCallback(e => OnFilterToggle(RefFilter.All, e.newValue));
            _filterRefToggle = new ToolbarToggle { text = "被引用" };
            _filterRefToggle.RegisterValueChangedCallback(e => OnFilterToggle(RefFilter.Referenced, e.newValue));
            _filterUnrefToggle = new ToolbarToggle { text = "未引用" };
            _filterUnrefToggle.RegisterValueChangedCallback(e => OnFilterToggle(RefFilter.Unreferenced, e.newValue));
            toolbar.Add(_filterAllToggle);
            toolbar.Add(_filterRefToggle);
            toolbar.Add(_filterUnrefToggle);

            toolbar.Add(new ToolbarSpacer());

            _sortNameToggle = new ToolbarToggle { text = "按名称", value = true };
            _sortNameToggle.RegisterValueChangedCallback(e => OnSortToggle(SortMode.Name, e.newValue));
            _sortCountToggle = new ToolbarToggle { text = "按引用数" };
            _sortCountToggle.RegisterValueChangedCallback(e => OnSortToggle(SortMode.RefCount, e.newValue));
            toolbar.Add(_sortNameToggle);
            toolbar.Add(_sortCountToggle);

            var flexSpacer = new ToolbarSpacer();
            flexSpacer.style.flexGrow = 1;
            toolbar.Add(flexSpacer);
            toolbar.Add(new ToolbarButton(Rescan) { text = "重新扫描" });
            root.Add(toolbar);

            // ── 左：图片列表 ──
            var left = new VisualElement();
            left.style.paddingTop = 4;
            left.style.paddingBottom = 4;
            _imageList = new ListView
            {
                fixedItemHeight = 26,
                selectionType = SelectionType.Single,
                makeItem = BuildImageRow,
                bindItem = BindImageRow
            };
            _imageList.style.flexGrow = 1;
            _imageList.selectionChanged += objs => ShowDetail(objs.FirstOrDefault() as ImageInfo);
            _imageList.itemsChosen += objs =>
            {
                var info = objs.FirstOrDefault() as ImageInfo;
                if (info != null)
                    PingAsset(info.Path);
            };
            left.Add(_imageList);

            // ── 右：引用详情 ──
            var right = new VisualElement();
            right.style.paddingTop = 4;
            right.style.paddingLeft = 8;
            right.style.paddingRight = 8;
            right.style.paddingBottom = 4;

            _emptyHint = new Label("在左侧选择一张图片查看其引用");
            _emptyHint.style.flexGrow = 1;
            _emptyHint.style.unityTextAlign = TextAnchor.MiddleLeft;
            right.Add(_emptyHint);

            _detailRoot = new VisualElement();
            _detailRoot.style.flexGrow = 1;
            _detailRoot.style.display = DisplayStyle.None;

            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.marginBottom = 6;
            _detailIcon = new Image();
            _detailIcon.style.width = 128;
            _detailIcon.style.height = 128;
            _detailIcon.style.marginRight = 10;
            _detailIcon.scaleMode = ScaleMode.ScaleToFit;
            _detailIcon.style.backgroundColor = new Color(0.16f, 0.16f, 0.16f);
            var titleCol = new VisualElement();
            titleCol.style.justifyContent = Justify.Center;
            _detailName = new Label();
            _detailName.style.fontSize = 16;
            _detailPath = new Label();
            _detailPath.style.fontSize = 10;
            _detailPath.style.color = new Color(0.6f, 0.6f, 0.6f);
            _detailCount = new Label();
            _detailCount.style.marginTop = 4;
            titleCol.Add(_detailName);
            titleCol.Add(_detailPath);
            titleCol.Add(_detailCount);
            header.Add(_detailIcon);
            header.Add(titleCol);
            _detailRoot.Add(header);

            _refList = new ListView
            {
                fixedItemHeight = 26,
                selectionType = SelectionType.Single,
                makeItem = BuildRefRow,
                bindItem = BindRefRow
            };
            _refList.style.flexGrow = 1;
            _refList.itemsChosen += objs =>
            {
                var path = objs.FirstOrDefault() as string;
                if (path != null)
                    PingAsset(path);
            };
            _detailRoot.Add(_refList);
            right.Add(_detailRoot);

            var split = new TwoPaneSplitView(0, 430, TwoPaneSplitViewOrientation.Horizontal);
            split.style.flexGrow = 1;
            split.Add(left);
            split.Add(right);
            root.Add(split);

            // ── 底部状态栏 ──
            var status = new VisualElement();
            status.style.flexDirection = FlexDirection.Row;
            status.style.paddingLeft = 4;
            status.style.paddingTop = 2;
            status.style.paddingBottom = 2;
            _statusLabel = new Label("未扫描");
            _statusLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
            status.Add(_statusLabel);
            var hint = new Label(UnrefHint);
            hint.style.fontSize = 10;
            hint.style.color = new Color(0.6f, 0.6f, 0.6f);
            hint.style.marginLeft = new Length(8, LengthUnit.Percent);
            hint.style.flexShrink = 0;
            status.Add(hint);
            root.Add(status);

            // 首次打开自动扫描（域重载后数据丢失也会走到这里重新扫）
            if (_all == null)
                Rescan();
        }

        private void OnDisable()
        {
            ReleasePreview();
        }

        /// <summary>
        /// 预览纹理优先从磁盘源文件解码（PNG/JPG 原始像素，绕开导入管线的 DXT/BC 压缩，
        /// 小图 1:1 显示不发虚）；其它格式（psd/tga 等）回退加载导入后的资产纹理。
        /// </summary>
        private static Texture2D LoadPreviewTexture(string path, out bool isRuntime)
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".png" || ext == ".jpg" || ext == ".jpeg")
            {
                try
                {
                    var bytes = File.ReadAllBytes(path);
                    var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    if (tex.LoadImage(bytes, false))
                    {
                        isRuntime = true;
                        return tex;
                    }
                    DestroyImmediate(tex);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            isRuntime = false;
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private void ReleasePreview()
        {
            if (_detailPreview == null)
                return;
            _detailIcon.image = null;
            if (_previewIsRuntime)
                DestroyImmediate(_detailPreview);
            else
                UnityEngine.Resources.UnloadAsset(_detailPreview);
            _detailPreview = null;
        }

        /// <summary>建立 图片 → 引用者 反向索引。返回 false 表示被用户取消。</summary>
        private bool Scan()
        {
            _all = null;
            _currentDetail = null;
            _typeCache.Clear();
            var sw = Stopwatch.StartNew();

            try
            {
                var images = new Dictionary<string, ImageInfo>(StringComparer.OrdinalIgnoreCase);
                var referencers = new List<string>();
                foreach (var p in AssetDatabase.GetAllAssetPaths())
                {
                    if (!p.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                        continue;
                    var ext = Path.GetExtension(p);
                    if (ImageExts.Contains(ext))
                        images[p] = new ImageInfo { Path = p, Name = Path.GetFileNameWithoutExtension(p) };
                    else if (ReferencerExts.Contains(ext))
                        referencers.Add(p);
                }

                for (var i = 0; i < referencers.Count; i++)
                {
                    var rp = referencers[i];
                    if (EditorUtility.DisplayCancelableProgressBar(
                            "图片引用查看器", $"分析依赖 {Path.GetFileName(rp)}（{i + 1}/{referencers.Count}）",
                            (float)i / referencers.Count))
                        return false;

                    // 递归依赖：材质引用的贴图也会记到使用该材质的预制体头上
                    foreach (var dep in AssetDatabase.GetDependencies(rp, true))
                    {
                        if (images.TryGetValue(dep, out var info))
                            info.Referencers.Add(rp);
                    }
                }

                _all = images.Values.OrderBy(i => i.Name, StringComparer.CurrentCulture).ToList();
                _scanSeconds = sw.ElapsedMilliseconds / 1000f;
                return true;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private void Rescan()
        {
            if (!Scan())
            {
                _statusLabel.text = "扫描已取消，点击“重新扫描”重试";
                return;
            }
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            if (_all == null)
                return;

            IEnumerable<ImageInfo> q = _all;
            if (!string.IsNullOrEmpty(_folderFilter))
                q = q.Where(i => i.Path.StartsWith(_folderFilter + "/", StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrEmpty(_search))
                q = q.Where(i => i.Path.IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0);
            q = _filter switch
            {
                RefFilter.Referenced => q.Where(i => i.Referencers.Count > 0),
                RefFilter.Unreferenced => q.Where(i => i.Referencers.Count == 0),
                _ => q
            };
            _view = (_sortMode == SortMode.Name
                ? q.OrderBy(i => i.Name, StringComparer.CurrentCulture).ThenBy(i => i.Path)
                : q.OrderByDescending(i => i.Referencers.Count).ThenBy(i => i.Name, StringComparer.CurrentCulture)
            ).ToList();

            _imageList.itemsSource = _view;
            _imageList.Rebuild();
            UpdateStatus();
        }

        private void UpdateStatus()
        {
            if (_all == null)
            {
                _statusLabel.text = "未扫描";
                return;
            }
            var unrefCount = _all.Count(i => i.Referencers.Count == 0);
            var scope = string.IsNullOrEmpty(_folderFilter) ? "" : $"，范围 {_folderFilter}";
            var text = $"共 {_all.Count} 张图片，{unrefCount} 张未被引用{scope}（扫描耗时 {_scanSeconds:0.0}s）";
            if (_view.Count != _all.Count)
                text += $",当前显示 {_view.Count} 张";
            _statusLabel.text = text;
        }

        private void ShowDetail(ImageInfo info)
        {
            _currentDetail = info;
            if (info == null)
                return;

            _emptyHint.style.display = DisplayStyle.None;
            _detailRoot.style.display = DisplayStyle.Flex;
            // 预览必须用源文件原始像素：导入管线对小图有 DXT/BC 块压缩，"发虚"的根源就在这
            ReleasePreview();
            _detailPreview = LoadPreviewTexture(info.Path, out _previewIsRuntime);
            _detailIcon.image = _detailPreview;
            // 显示区跟随源图尺寸：小图 1:1 最锐利，大图封顶 256
            _detailIcon.style.width = _detailPreview != null ? Math.Min(_detailPreview.width, 256) : 128;
            _detailIcon.style.height = _detailPreview != null ? Math.Min(_detailPreview.height, 256) : 128;
            _detailPath.text = _detailPreview != null
                ? $"{info.Path}（源图 {_detailPreview.width}x{_detailPreview.height}）"
                : info.Path;
            if (info.Icon == null)
                info.Icon = AssetDatabase.GetCachedIcon(info.Path);
            _detailIcon.image = info.Icon;
            _detailName.text = info.Name;
            _detailPath.text = info.Path;
            if (info.Referencers.Count == 0)
            {
                _detailCount.text = "未被任何资源引用";
                _detailCount.style.color = new Color(1f, 0.55f, 0.25f);
            }
            else
            {
                _detailCount.text = $"被 {info.Referencers.Count} 个资源引用";
                _detailCount.style.color = new Color(0.6f, 0.6f, 0.6f);
            }
            _refList.itemsSource = info.Referencers;
            _refList.Rebuild();
        }

        private void OnFilterToggle(RefFilter target, bool on)
        {
            if (_syncing || !on)
                return;
            _filter = target;
            _syncing = true;
            _filterAllToggle.SetValueWithoutNotify(target == RefFilter.All);
            _filterRefToggle.SetValueWithoutNotify(target == RefFilter.Referenced);
            _filterUnrefToggle.SetValueWithoutNotify(target == RefFilter.Unreferenced);
            _syncing = false;
            ApplyFilter();
        }

        private void OnSortToggle(SortMode target, bool on)
        {
            if (_syncing || !on)
                return;
            _sortMode = target;
            _syncing = true;
            _sortNameToggle.SetValueWithoutNotify(target == SortMode.Name);
            _sortCountToggle.SetValueWithoutNotify(target == SortMode.RefCount);
            _syncing = false;
            ApplyFilter();
        }

        private VisualElement BuildImageRow()
        {
            return new ImageRow();
        }

        private void BindImageRow(VisualElement element, int i)
        {
            var row = (ImageRow)element;
            var info = _view[i];
            if (info.Icon == null)
                info.Icon = AssetDatabase.GetCachedIcon(info.Path);
            row.Icon.image = info.Icon;
            row.Name.text = info.Name;
            if (info.Referencers.Count == 0)
            {
                row.Count.text = "未引用";
                row.Count.style.color = new Color(1f, 0.55f, 0.25f);
            }
            else
            {
                row.Count.text = $"{info.Referencers.Count} 处引用";
                row.Count.style.color = new Color(0.55f, 0.55f, 0.55f);
            }
        }

        private VisualElement BuildRefRow()
        {
            return new RefRow();
        }

        private void BindRefRow(VisualElement element, int i)
        {
            var row = (RefRow)element;
            var path = _currentDetail.Referencers[i];
            row.Icon.image = AssetDatabase.GetCachedIcon(path);
            row.Name.text = Path.GetFileNameWithoutExtension(path);
            if (!_typeCache.TryGetValue(path, out var label))
                _typeCache[path] = label = TypeLabelOf(path);
            row.Type.text = $"[{label}]";
            row.Path.text = path;
        }

        private static string TypeLabelOf(string path)
        {
            switch (Path.GetExtension(path).ToLowerInvariant())
            {
                case ".prefab": return "预制体";
                case ".unity": return "场景";
                case ".mat": return "材质";
                case ".spriteatlas": return "图集";
                case ".controller":
                case ".overridecontroller": return "动画";
                case ".fontsettings": return "字体";
                case ".playable": return "Playable";
                case ".mask": return "遮罩";
                default: return "资源";
            }
        }

        private static void PingAsset(string path)
        {
            var asset = AssetDatabase.LoadAssetAtPath<Object>(path);
            if (asset == null)
                return;
            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
        }
    }
}

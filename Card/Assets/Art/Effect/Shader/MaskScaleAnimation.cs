using UnityEngine;
using UnityEngine.UI;

// 遮罩缩放动画：让 MaskScaleReveal 材质的遮罩区域按曲线从起始大小过渡到结束大小
// 同时支持普通Renderer和UGUI的Graphic(Image)
// 大小参数使用百分比：100 = 原始大小，0 = 完全隐藏
public class MaskScaleAnimation : MonoBehaviour
{
    [Header("缩放动画(百分比,100=原始大小,0=隐藏)")]
    [SerializeField] private float startScalePct = 100f;
    [SerializeField] private float endScalePct = 0f;
    [SerializeField] private float duration = 1f;
    [SerializeField] private AnimationCurve curve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("偏移动画(可关闭)")]
    [SerializeField] private bool animateOffset = false;
    [SerializeField] private float offsetXAmplitude = 0.1f;
    [SerializeField] private float offsetYAmplitude = 0.1f;
    [SerializeField] private float offsetSpeed = 1.5f;

    private Renderer _renderer;
    private MaterialPropertyBlock _mpb;

    private Graphic _graphic;
    private Material _uiMat; // UGUI下实例化材质，避免改动共享材质

    private float _elapsed;

    private static readonly int MaskScaleId = Shader.PropertyToID("_MaskScale");
    private static readonly int MaskOffsetXId = Shader.PropertyToID("_MaskOffsetX");
    private static readonly int MaskOffsetYId = Shader.PropertyToID("_MaskOffsetY");

    void Start()
    {
        _renderer = GetComponent<Renderer>();
        _mpb = new MaterialPropertyBlock();

        _graphic = GetComponent<Graphic>();
        if (_graphic != null)
        {
            if (_graphic.material != null)
                _uiMat = new Material(_graphic.material);
            else
                _uiMat = new Material(Shader.Find("Effect/MaskScaleReveal"));
            _graphic.material = _uiMat;
        }

        ApplyScale(startScalePct);
    }

    void Update()
    {
        _elapsed += Time.deltaTime;
        float t = duration > 0.0001f ? Mathf.Clamp01(_elapsed / duration) : 1f;
        // 曲线可选：未编辑(无关键帧)时退化为线性
        float e = (curve != null && curve.keys != null && curve.keys.Length > 0)
            ? curve.Evaluate(t)
            : t;
        ApplyScale(Mathf.LerpUnclamped(startScalePct, endScalePct, e));

        float ox = animateOffset ? offsetXAmplitude * Mathf.Sin(Time.time * offsetSpeed) : 0f;
        float oy = animateOffset ? offsetYAmplitude * Mathf.Sin(Time.time * offsetSpeed) : 0f;
        if (_renderer != null)
        {
            _mpb.SetFloat(MaskOffsetXId, ox);
            _mpb.SetFloat(MaskOffsetYId, oy);
        }
        else if (_uiMat != null)
        {
            _uiMat.SetFloat(MaskOffsetXId, ox);
            _uiMat.SetFloat(MaskOffsetYId, oy);
        }
    }

    // 外部可调用：重新播放动画
    public void Replay()
    {
        _elapsed = 0f;
    }

    private void ApplyScale(float pct)
    {
        float scale = pct * 0.01f;
        if (_renderer != null)
        {
            _renderer.GetPropertyBlock(_mpb);
            _mpb.SetFloat(MaskScaleId, scale);
            _renderer.SetPropertyBlock(_mpb);
        }
        else if (_uiMat != null)
        {
            _uiMat.SetFloat(MaskScaleId, scale);
        }
    }

    void OnDestroy()
    {
        if (_uiMat != null)
            Destroy(_uiMat);
    }
}

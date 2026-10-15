using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace App.UI
{
    /// <summary>
    /// 挂到节点上并启用：自身和子节点里的 Image、RawImage 用 UI/Gray，
    /// TextMeshPro 用 TextMeshPro/Distance Field Gray。取消勾选或禁用物体后恢复原材质。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UIGrayscale : MonoBehaviour
    {
        private const string ImageShaderName = "UI/Gray";
        private const string TextShaderName = "TextMeshPro/Distance Field Gray";

        private static readonly int GrayId = Shader.PropertyToID("_Gray");
        private static Shader _imageShader;
        private static Shader _textShader;

        private readonly List<Graphic> _graphics = new List<Graphic>(8);
        private readonly List<Material> _originals = new List<Material>(8);
        private readonly List<Material> _instances = new List<Material>(8);

        private void OnEnable()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            Apply();
        }

        private void OnDisable()
        {
            Restore();
        }

        private void Apply()
        {
            var imageShader = ImageShader();
            var textShader = TextShader();
            var found = GetComponentsInChildren<MaskableGraphic>(true);
            for (var i = 0; i < found.Length; i++)
            {
                var graphic = found[i];
                if (graphic == null || !OwnedByThis(graphic))
                {
                    continue;
                }

                var shader = ShaderFor(graphic, imageShader, textShader);
                if (shader == null)
                {
                    continue;
                }

                var mat = new Material(shader)
                {
                    name = "UIGray (Instance)",
                    hideFlags = HideFlags.HideAndDontSave
                };
                var source = SourceMaterial(graphic);
                if (source != null)
                {
                    mat.CopyPropertiesFromMaterial(source);
                    mat.shaderKeywords = source.shaderKeywords;
                }

                mat.SetFloat(GrayId, 1f);
                _graphics.Add(graphic);
                _originals.Add(source);
                _instances.Add(mat);
                Assign(graphic, mat);
            }
        }

        private void Restore()
        {
            for (var i = 0; i < _graphics.Count; i++)
            {
                if (_graphics[i] != null)
                {
                    Assign(_graphics[i], _originals[i]);
                }
            }

            for (var i = 0; i < _instances.Count; i++)
            {
                if (_instances[i] != null)
                {
                    Destroy(_instances[i]);
                }
            }

            _graphics.Clear();
            _originals.Clear();
            _instances.Clear();
        }

        private bool OwnedByThis(Graphic graphic)
        {
            var owners = graphic.GetComponentsInParent<UIGrayscale>(true);
            for (var i = 0; i < owners.Length; i++)
            {
                if (!owners[i].isActiveAndEnabled)
                {
                    continue;
                }

                return owners[i] == this;
            }

            return false;
        }

        private static Shader ShaderFor(Graphic graphic, Shader imageShader, Shader textShader)
        {
            if (graphic is TMP_Text)
            {
                return textShader;
            }

            if (graphic is Image || graphic is RawImage)
            {
                return imageShader;
            }

            return null;
        }

        private static Material SourceMaterial(Graphic graphic)
        {
            if (graphic is TMP_Text tmp)
            {
                return tmp.fontSharedMaterial;
            }

            var current = graphic.material;
            return current != null && current != graphic.defaultMaterial ? current : null;
        }

        private static void Assign(Graphic graphic, Material material)
        {
            if (graphic is TMP_Text tmp)
            {
                tmp.fontSharedMaterial = material;
                return;
            }

            graphic.material = material;
        }

        private static Shader ImageShader()
        {
            if (_imageShader == null)
            {
                _imageShader = Shader.Find(ImageShaderName);
            }

            return _imageShader;
        }

        private static Shader TextShader()
        {
            if (_textShader == null)
            {
                _textShader = Shader.Find(TextShaderName);
            }

            return _textShader;
        }
    }
}

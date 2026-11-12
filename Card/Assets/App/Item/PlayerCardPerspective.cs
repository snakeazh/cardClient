using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace App.Game
{
    /// <summary>在同一卡面坐标系中投影图片和文字，让血条、字形与底图一起倾斜。</summary>
    [ExecuteAlways]
    public sealed class PlayerCardPerspective : BaseMeshEffect
    {
        private RectTransform _frame;
        private TMP_Text _text;
        private float _inverseDepth;
        private float _heightScale = 1f;

        public void Initialize(RectTransform frame)
        {
            UnsubscribeText();
            _frame = frame;
            _text = graphic as TMP_Text;
            if (_text != null && isActiveAndEnabled)
                _text.OnPreRenderText += ProjectText;
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            if (_text != null)
            {
                _text.OnPreRenderText -= ProjectText;
                _text.OnPreRenderText += ProjectText;
            }
        }

        protected override void OnDisable()
        {
            UnsubscribeText();
            base.OnDisable();
        }

        protected override void OnDestroy()
        {
            UnsubscribeText();
            base.OnDestroy();
        }

        private void UnsubscribeText()
        {
            if (_text != null)
                _text.OnPreRenderText -= ProjectText;
        }

        public void SetProjection(float inverseDepth, float heightScale)
        {
            _inverseDepth = inverseDepth;
            _heightScale = heightScale;
            if (!isActiveAndEnabled)
                return;

            if (_text != null)
                _text.havePropertiesChanged = true;
            else
                graphic.SetVerticesDirty();
        }

        public override void ModifyMesh(VertexHelper vertices)
        {
            if (!IsActive() || _frame == null || _inverseDepth == 0f)
                return;

            var toFrame = _frame.worldToLocalMatrix * transform.localToWorldMatrix;
            var fromFrame = toFrame.inverse;
            if (vertices.currentVertCount == 4 && graphic is Image image &&
                image.type == Image.Type.Simple && !image.useSpriteMesh)
                SubdivideTallSprite(vertices, toFrame);

            var vertex = new UIVertex();
            for (var i = 0; i < vertices.currentVertCount; i++)
            {
                vertices.PopulateUIVertex(ref vertex, i);
                vertex.position = Project(vertex.position, toFrame, fromFrame);
                vertices.SetUIVertex(vertex, i);
            }
        }

        // 正交 UI 在每个三角形内线性采样。细分较高的图片，避免头像中央沿对角线错位。
        private void SubdivideTallSprite(VertexHelper vertices, in Matrix4x4 toFrame)
        {
            var bottomLeft = new UIVertex();
            var topLeft = new UIVertex();
            var bottomRight = new UIVertex();
            var topRight = new UIVertex();
            vertices.PopulateUIVertex(ref bottomLeft, 0);
            vertices.PopulateUIVertex(ref topLeft, 1);
            vertices.PopulateUIVertex(ref topRight, 2);
            vertices.PopulateUIVertex(ref bottomRight, 3);
            var height = Mathf.Abs(toFrame.MultiplyVector(topLeft.position - bottomLeft.position).y);
            var rows = Mathf.Clamp(Mathf.CeilToInt(height * 8f / Mathf.Max(1f, _frame.rect.height)), 1, 8);
            if (rows == 1)
                return;

            vertices.Clear();
            for (var row = 0; row <= rows; row++)
            {
                var t = (float)row / rows;
                var left = bottomLeft;
                var right = bottomRight;
                left.position = Vector3.Lerp(bottomLeft.position, topLeft.position, t);
                left.uv0 = Vector4.Lerp(bottomLeft.uv0, topLeft.uv0, t);
                right.position = Vector3.Lerp(bottomRight.position, topRight.position, t);
                right.uv0 = Vector4.Lerp(bottomRight.uv0, topRight.uv0, t);
                vertices.AddVert(left);
                vertices.AddVert(right);
                if (row == 0)
                    continue;

                var i = (row - 1) * 2;
                vertices.AddTriangle(i, i + 2, i + 3);
                vertices.AddTriangle(i + 3, i + 1, i);
            }
        }

        // TMP 自己上传网格，不经过 Graphic 的 IMeshModifier。
        private void ProjectText(TMP_TextInfo info)
        {
            if (!IsActive() || _inverseDepth == 0f)
                return;

            var toFrame = _frame.worldToLocalMatrix * transform.localToWorldMatrix;
            var fromFrame = toFrame.inverse;
            for (var m = 0; m < info.materialCount; m++)
            {
                var mesh = info.meshInfo[m];
                for (var i = 0; i < mesh.vertexCount; i++)
                    mesh.vertices[i] = Project(mesh.vertices[i], toFrame, fromFrame);
            }
        }

        private Vector3 Project(Vector3 vertex, in Matrix4x4 toFrame, in Matrix4x4 fromFrame)
        {
            var point = toFrame.MultiplyPoint3x4(vertex);
            var scale = 1f / Mathf.Max(0.25f, 1f + point.y * _inverseDepth);
            point.x *= scale;
            point.y *= scale * _heightScale;
            return fromFrame.MultiplyPoint3x4(point);
        }
    }
}

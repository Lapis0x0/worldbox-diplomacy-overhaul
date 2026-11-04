using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace DiplomacyOverhaul
{
    // 集团的底色：议席半圆里一段扇环，或者坐标图里把成员圈起来的一块（成员的凸包向外扩一圈圆角）。
    // 坐标照 UiKit：以父节点左上角为原点，y 向下。颜色用 Graphic.color，选中时调透明度就能淡入淡出
    public class BlocShape : MaskableGraphic
    {
        readonly List<Vector2> _outline = new List<Vector2>();
        float _cx, _cy, _rIn, _rOut, _t1, _t2;
        bool _sector;

        // 扇环：从角度 pT1 顺时针到 pT2（弧度，0 朝右、π/2 朝上）
        public void Sector(float pCx, float pCy, float pRIn, float pROut, float pT1, float pT2)
        {
            _sector = true;
            _cx = pCx;
            _cy = pCy;
            _rIn = pRIn;
            _rOut = pROut;
            _t1 = pT1;
            _t2 = pT2;
            SetVerticesDirty();
        }

        // 圈住这些点的一块：每个点换成半径 pRadius 的一圈，再取整体的凸包
        public void Hull(List<Vector2> pPoints, float pRadius)
        {
            _sector = false;
            var around = new List<Vector2>();
            const int n = 16;
            foreach (Vector2 p in pPoints)
                for (int i = 0; i < n; i++)
                {
                    float a = Mathf.PI * 2f * i / n;
                    around.Add(p + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * pRadius);
                }
            _outline.Clear();
            _outline.AddRange(ConvexHull(around));
            SetVerticesDirty();
        }

        static List<Vector2> ConvexHull(List<Vector2> pPoints)
        {
            var p = new List<Vector2>(pPoints);
            p.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
            if (p.Count < 3) return p;
            var hull = new List<Vector2>();
            float Cross(Vector2 o, Vector2 a, Vector2 b) => (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);
            for (int pass = 0; pass < 2; pass++)
            {
                int start = hull.Count;
                for (int i = 0; i < p.Count; i++)
                {
                    Vector2 q = pass == 0 ? p[i] : p[p.Count - 1 - i];
                    while (hull.Count >= start + 2 && Cross(hull[hull.Count - 2], hull[hull.Count - 1], q) <= 0f) hull.RemoveAt(hull.Count - 1);
                    hull.Add(q);
                }
                hull.RemoveAt(hull.Count - 1);
            }
            return hull;
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = rectTransform.rect;
            Vector3 At(float x, float y) => new Vector3(r.xMin + x, r.yMax - y);
            if (_sector)
            {
                int steps = Mathf.Max(2, Mathf.CeilToInt((_t1 - _t2) / 0.04f));
                for (int i = 0; i <= steps; i++)
                {
                    float t = Mathf.Lerp(_t1, _t2, i / (float)steps);
                    float c = Mathf.Cos(t), s = Mathf.Sin(t);
                    vh.AddVert(At(_cx + _rOut * c, _cy - _rOut * s), color, Vector2.zero);
                    vh.AddVert(At(_cx + _rIn * c, _cy - _rIn * s), color, Vector2.zero);
                    if (i == 0) continue;
                    int v = i * 2;
                    vh.AddTriangle(v - 2, v, v + 1);
                    vh.AddTriangle(v - 2, v + 1, v - 1);
                }
                return;
            }
            if (_outline.Count < 3) return;
            Vector2 center = Vector2.zero;
            foreach (Vector2 p in _outline) center += p;
            center /= _outline.Count;
            vh.AddVert(At(center.x, center.y), color, Vector2.zero);
            foreach (Vector2 p in _outline) vh.AddVert(At(p.x, p.y), color, Vector2.zero);
            for (int i = 0; i < _outline.Count; i++) vh.AddTriangle(0, 1 + i, 1 + (i + 1) % _outline.Count);
        }
    }
}

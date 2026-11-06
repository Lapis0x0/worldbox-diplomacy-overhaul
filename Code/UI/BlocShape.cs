using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace DiplomacyOverhaul
{
    // 集团的底色：议席半圆里一段扇环，或者坐标图里把成员圈起来的一块（成员的凸包向外扩一圈圆角）。
    // 坐标照 UiKit：以父节点左上角为原点，y 向下。颜色用 Graphic.color，选中时调透明度就能淡入淡出。
    // stroke 大于 0 时只画边框：沿形状的边往里收这么宽，相邻两段扇环的边框并排成一道分隔线
    public class BlocShape : MaskableGraphic
    {
        public float stroke;
        readonly List<Vector2> _outline = new List<Vector2>();
        float _cx, _cy, _rIn, _rOut, _t1, _t2, _padStart, _padEnd;
        bool _sector;

        // 扇环：从角度 pT1 顺时针到 pT2（弧度，0 朝右、π/2 朝上）。pPadStart、pPadEnd 大于 0 时，
        // 那一头的边再竖直往下延这么长（用在半圆两端，底边和席位平行）
        public void Sector(float pCx, float pCy, float pRIn, float pROut, float pT1, float pT2, float pPadStart = 0f, float pPadEnd = 0f)
        {
            _padStart = pPadStart;
            _padEnd = pPadEnd;
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
            Vector3 At(Vector2 p) => new Vector3(r.xMin + p.x, r.yMax - p.y);
            if (_sector)
            {
                int steps = Mathf.Max(2, Mathf.CeilToInt((_t1 - _t2) / 0.04f));
                var outer = new List<Vector2>();
                var inner = new List<Vector2>();
                for (int i = 0; i <= steps; i++)
                {
                    float t = Mathf.Lerp(_t1, _t2, i / (float)steps);
                    var dir = new Vector2(Mathf.Cos(t), -Mathf.Sin(t));
                    outer.Add(new Vector2(_cx, _cy) + dir * _rOut);
                    inner.Add(new Vector2(_cx, _cy) + dir * _rIn);
                }
                var down = new Vector2(0f, 1f);
                if (_padStart > 0f)
                {
                    outer.Insert(0, outer[0] + down * _padStart);
                    inner.Insert(0, inner[0] + down * _padStart);
                }
                if (_padEnd > 0f)
                {
                    outer.Add(outer[outer.Count - 1] + down * _padEnd);
                    inner.Add(inner[inner.Count - 1] + down * _padEnd);
                }
                if (stroke > 0f)
                {
                    inner.Reverse();
                    outer.AddRange(inner);
                    Stroke(vh, outer, At);
                    return;
                }
                for (int i = 0; i < outer.Count; i++)
                {
                    vh.AddVert(At(outer[i]), color, Vector2.zero);
                    vh.AddVert(At(inner[i]), color, Vector2.zero);
                    if (i == 0) continue;
                    int v = i * 2;
                    vh.AddTriangle(v - 2, v, v + 1);
                    vh.AddTriangle(v - 2, v + 1, v - 1);
                }
                return;
            }
            if (_outline.Count < 3) return;
            if (stroke > 0f) { Stroke(vh, _outline, At); return; }
            Vector2 center = Vector2.zero;
            foreach (Vector2 p in _outline) center += p;
            center /= _outline.Count;
            vh.AddVert(At(center), color, Vector2.zero);
            foreach (Vector2 p in _outline) vh.AddVert(At(p), color, Vector2.zero);
            for (int i = 0; i < _outline.Count; i++) vh.AddTriangle(0, 1 + i, 1 + (i + 1) % _outline.Count);
        }

        // 闭合轮廓往里收 stroke 宽的一圈
        void Stroke(VertexHelper vh, List<Vector2> pPath, System.Func<Vector2, Vector3> pAt)
        {
            int n = pPath.Count;
            float area = 0f;
            for (int i = 0; i < n; i++)
            {
                Vector2 a = pPath[i], b = pPath[(i + 1) % n];
                area += a.x * b.y - a.y * b.x;
            }
            float side = area > 0f ? 1f : -1f;
            for (int i = 0; i < n; i++)
            {
                Vector2 p = pPath[i];
                Vector2 e1 = (p - pPath[(i + n - 1) % n]).normalized, e2 = (pPath[(i + 1) % n] - p).normalized;
                Vector2 n1 = new Vector2(-e1.y, e1.x) * side, n2 = new Vector2(-e2.y, e2.x) * side;
                Vector2 m = (n1 + n2).normalized;
                float len = stroke / Mathf.Max(0.3f, Vector2.Dot(m, n1));
                vh.AddVert(pAt(p), color, Vector2.zero);
                vh.AddVert(pAt(p + m * len), color, Vector2.zero);
            }
            for (int i = 0; i < n; i++)
            {
                int a = i * 2, b = (i + 1) % n * 2;
                vh.AddTriangle(a, b, b + 1);
                vh.AddTriangle(a, b + 1, a + 1);
            }
        }
    }
}

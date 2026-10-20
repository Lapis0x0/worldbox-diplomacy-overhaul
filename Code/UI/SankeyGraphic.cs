using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace DiplomacyOverhaul
{
    // 流向图的带子：从左边一处流到右边一处，厚度不变，中段按 S 形弯过去。
    // 坐标照 UiKit：以图的左上角为原点，y 向下
    public class SankeyGraphic : MaskableGraphic
    {
        public struct Band
        {
            public float x0, y0, x1, y1, h;
            public Color color;
        }

        const int Steps = 24;
        readonly List<Band> _bands = new List<Band>();

        public void SetBands(List<Band> pBands)
        {
            _bands.Clear();
            _bands.AddRange(pBands);
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = rectTransform.rect;
            foreach (Band b in _bands)
            {
                int start = vh.currentVertCount;
                for (int i = 0; i <= Steps; i++)
                {
                    float t = i / (float)Steps;
                    float s = t * t * (3f - 2f * t);
                    float x = Mathf.Lerp(b.x0, b.x1, t);
                    float y = Mathf.Lerp(b.y0, b.y1, s);
                    vh.AddVert(new Vector3(r.xMin + x, r.yMax - y), b.color, Vector2.zero);
                    vh.AddVert(new Vector3(r.xMin + x, r.yMax - y - b.h), b.color, Vector2.zero);
                    if (i == 0) continue;
                    int v = start + i * 2;
                    vh.AddTriangle(v - 2, v, v + 1);
                    vh.AddTriangle(v - 2, v + 1, v - 1);
                }
            }
        }
    }
}

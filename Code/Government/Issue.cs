using System;

namespace DiplomacyOverhaul
{
    // 议题：几个选项依次排在一条政治轴上，各股势力按意识形态和处境决定想要哪一档
    public class Issue
    {
        public PolicyAxis axis;
        public int count = 3;
        // 意识形态在这个议题上的载荷：和所在的轴不相干的议题是 0
        public float load = 1f;
        // 处境和利益：把一股势力在这个议题上的理想位置往哪边挪多少（和意识形态同一个单位）
        public Func<Kingdom, ParliamentClan, float> lean;

        // 档位均分：-1 到 1 按档数等分，每一档代表其中一段，这是那一段的中心。
        // 三档是 -2/3、0、2/3，四档是 ±1/4、±3/4，两档是 ±1/2
        public float Center(int pOption) => count <= 1 ? 0f : -1f + (2f * pOption + 1f) / count;
    }
}

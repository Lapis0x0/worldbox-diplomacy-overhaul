using System;
using UnityEngine;
using UnityEngine.UI;

namespace DiplomacyOverhaul
{
    // 用原版贴图拼界面的小工具：坐标都从父节点左上角算，y 向下
    public static class UiKit
    {
        public static readonly Color Ink = Hex("#f4f1e8");
        public static readonly Color Muted = Hex("#cfc8b4");
        public static readonly Color Gold = Hex("#ffb52e");
        public static readonly Color HeaderGold = Hex("#ffa827");
        public static readonly Color Line = Hex("#5d6253");
        public static readonly Color Edge = Hex("#1a1a18");
        public const string Slot = "ui/special/windowInnerSlicedRound";

        public static Color Hex(string pHex)
        {
            ColorUtility.TryParseHtmlString(pHex, out Color c);
            return c;
        }

        public static string Tag(Color pColor) => "#" + ColorUtility.ToHtmlStringRGB(pColor);

        public static RectTransform Box(Transform pParent, float pX, float pY, float pW, float pH, string pName = "box")
        {
            var go = new GameObject(pName, typeof(RectTransform));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(pParent, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(pX, -pY);
            rt.sizeDelta = new Vector2(pW, pH);
            return rt;
        }

        public static RectTransform Sliced(Transform pParent, string pSprite, float pX, float pY, float pW, float pH)
        {
            RectTransform rt = Box(pParent, pX, pY, pW, pH, "slot");
            Image img = rt.gameObject.AddComponent<Image>();
            img.sprite = SpriteTextureLoader.getSprite(pSprite);
            img.type = Image.Type.Sliced;
            img.raycastTarget = false;
            return rt;
        }

        public static RectTransform Rect(Transform pParent, float pX, float pY, float pW, float pH, Color pColor)
        {
            RectTransform rt = Box(pParent, pX, pY, pW, pH, "rect");
            Image img = rt.gameObject.AddComponent<Image>();
            img.color = pColor;
            img.raycastTarget = false;
            return rt;
        }

        // 带深色描边的色块：议席、氏族的派系色
        public static RectTransform Swatch(Transform pParent, float pX, float pY, float pSize, Color pColor, float pAlpha)
        {
            RectTransform rt = Rect(pParent, pX, pY, pSize, pSize, new Color(pColor.r, pColor.g, pColor.b, pAlpha));
            var outline = rt.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(Edge.r, Edge.g, Edge.b, pAlpha);
            outline.effectDistance = new Vector2(0.5f, -0.5f);
            return rt;
        }

        public static RectTransform Icon(Transform pParent, string pSprite, float pX, float pY, float pSize) =>
            Picture(pParent, SpriteTextureLoader.getSprite(pSprite), pX, pY, pSize, pSize);

        public static RectTransform Picture(Transform pParent, Sprite pSprite, float pX, float pY, float pW, float pH)
        {
            RectTransform rt = Box(pParent, pX, pY, pW, pH, "icon");
            Image img = rt.gameObject.AddComponent<Image>();
            img.sprite = pSprite;
            img.preserveAspect = true;
            img.raycastTarget = false;
            return rt;
        }

        // 单位头像：原版界面里头像用的同一张贴图，深色底框；点一下打开这个人的单位窗口
        public static RectTransform Avatar(Transform pParent, Actor pActor, float pX, float pY, float pSize)
        {
            RectTransform frame = Rect(pParent, pX, pY, pSize, pSize, Hex("#2c2f29"));
            var outline = frame.gameObject.AddComponent<Outline>();
            outline.effectColor = Edge;
            outline.effectDistance = new Vector2(0.8f, -0.8f);
            Sprite sprite = AvatarSprite(pActor);
            if (sprite != null) Picture(frame, sprite, pSize * 0.1f, pSize * 0.1f, pSize * 0.8f, pSize * 0.8f);
            if (pActor != null && pActor.isAlive())
            {
                Image bg = frame.GetComponent<Image>();
                bg.raycastTarget = true;
                var button = frame.gameObject.AddComponent<UnityEngine.UI.Button>();
                button.targetGraphic = bg;
                button.onClick.AddListener(() => { if (pActor.isAlive()) ActionLibrary.openUnitWindow(pActor); });
            }
            return frame;
        }

        static Sprite AvatarSprite(Actor pActor)
        {
            if (pActor == null || !pActor.isAlive()) return null;
            try
            {
                var data = new ActorAvatarData();
                data.setData(pActor);
                AnimationContainerUnit cont = DynamicActorSpriteCreatorUI.getContainerForUI(pActor);
                return data.getColoredSprite(cont.walking.frames[0], cont);
            }
            catch (Exception)
            {
                return null;
            }
        }

        // 家族旗帜：和原版家族旗帜一样，底图染副色，图案染旗色
        public static RectTransform ClanBanner(Transform pParent, Clan pClan, float pX, float pY, float pW, float pH)
        {
            RectTransform rt = Box(pParent, pX, pY, pW, pH, "banner");
            if (pClan == null) return rt;
            ColorAsset color = pClan.getColor();
            Picture(rt, pClan.getBackgroundSprite(), 0f, 0f, pW, pH).GetComponent<Image>().color = color.getColorMainSecond();
            Picture(rt, pClan.getIconSprite(), 0f, 0f, pW, pH).GetComponent<Image>().color = color.getColorBanner();
            return rt;
        }

        public static Color ClanColor(Clan pClan) => pClan != null ? pClan.getColor().getColorMain() : Muted;

        // 写字用的家族色：太暗的颜色在深色底上看不清，往白里提亮
        public static Color ClanText(Clan pClan)
        {
            Color c = ClanColor(pClan);
            float luma = 0.299f * c.r + 0.587f * c.g + 0.114f * c.b;
            return luma < 0.45f ? Color.Lerp(c, Color.white, (0.45f - luma) / 0.45f * 0.7f) : c;
        }

        // 选中的黄框
        public static RectTransform Frame(RectTransform pTarget, float pInset)
        {
            RectTransform rt = Sliced(pTarget, "ui/special/buttonSelected", 0f, 0f, 0f, 0f);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(pInset, pInset);
            rt.offsetMax = new Vector2(-pInset, -pInset);
            rt.GetComponent<Image>().fillCenter = false;
            return rt;
        }

        public static RectTransform Button(Transform pParent, float pX, float pY, float pW, float pH, string pSprite, Action pClick)
        {
            RectTransform rt = Box(pParent, pX, pY, pW, pH, "button");
            Image img = rt.gameObject.AddComponent<Image>();
            if (pSprite != null)
            {
                img.sprite = SpriteTextureLoader.getSprite(pSprite);
                img.type = Image.Type.Sliced;
            }
            else img.color = new Color(0f, 0f, 0f, 0f);
            var button = rt.gameObject.AddComponent<UnityEngine.UI.Button>();
            button.targetGraphic = img;
            button.onClick.AddListener(() => pClick());
            return rt;
        }

        public static RectTransform Text(Transform pParent, string pText, float pX, float pY, float pW, float pH, int pSize, Color pColor, TextAnchor pAlign)
        {
            RectTransform rt = Box(pParent, pX, pY, pW, pH, "text");
            UnityEngine.UI.Text t = rt.gameObject.AddComponent<UnityEngine.UI.Text>();
            t.font = LocalizedTextManager.current_font;
            t.fontSize = pSize;
            t.color = pColor;
            t.alignment = pAlign;
            t.supportRichText = true;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            t.text = pText;
            var shadow = rt.gameObject.AddComponent<Shadow>();
            shadow.effectColor = Hex("#151515");
            shadow.effectDistance = new Vector2(0.6f, -0.6f);
            return rt;
        }

        public static float Measure(string pText, int pSize)
        {
            Font font = LocalizedTextManager.current_font;
            font.RequestCharactersInTexture(pText, pSize);
            float w = 0f;
            foreach (char c in pText)
            {
                if (font.GetCharacterInfo(c, out CharacterInfo info, pSize)) w += info.advance;
                else w += pSize;
            }
            return w;
        }

        // 放不下时截短，末尾加省略号
        public static string Fit(string pText, int pSize, float pWidth)
        {
            if (Measure(pText, pSize) <= pWidth) return pText;
            for (int n = pText.Length - 1; n > 0; n--)
            {
                string s = pText.Substring(0, n) + "…";
                if (Measure(s, pSize) <= pWidth) return s;
            }
            return "…";
        }

        // 分区标题条：原版的标签页底图，两端各一个图标
        public static void Header(Transform pParent, float pX, float pY, float pW, string pText, string pIcon)
        {
            RectTransform bar = Sliced(pParent, "ui/special/background_tab_content", pX, pY, pW, 20f);
            Icon(bar, pIcon, 6f, 3f, 14f);
            Icon(bar, pIcon, pW - 20f, 3f, 14f);
            Text(bar, pText, 0f, 0f, pW, 18f, 9, HeaderGold, TextAnchor.MiddleCenter);
        }

        // 能滚动的列表，返回内容节点
        public static RectTransform Scroll(Transform pParent, string pName, float pX, float pY, float pW, float pH, float pContentH)
        {
            RectTransform view = Box(pParent, pX, pY, pW, pH, pName);
            view.gameObject.AddComponent<RectMask2D>();
            Image hit = view.gameObject.AddComponent<Image>();
            hit.color = new Color(0f, 0f, 0f, 0f);
            RectTransform content = Box(view, 0f, 0f, pW, Mathf.Max(pH, pContentH), "content");
            var scroll = view.gameObject.AddComponent<ScrollRect>();
            scroll.content = content;
            scroll.horizontal = false;
            scroll.vertical = pContentH > pH;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 8f;
            return content;
        }

        // 半圆的排法：试 1–16 排，挑方块沿弧和沿半径的间距最接近的那种；每排的席位数和半径成正比。
        // 内径固定；席位够密就铺满，每席平均占的环面积超过 400 时外径往里收，不把几个方块撒满整块地方。
        // 返回的位置从左到右、同一角度从内到外
        public static System.Collections.Generic.List<Vector3> SeatLayout(int pCount, float pW, float pH, out float pSize)
        {
            var result = new System.Collections.Generic.List<Vector3>();
            pSize = 6f;
            if (pCount <= 0) return result;
            float rFull = pW / 2f - 4f, rIn = rFull * 0.465f;
            float cx = pW / 2f, cy = pH - 3f, rOut = Mathf.Clamp(Mathf.Sqrt(rIn * rIn + 400f * 2f / Mathf.PI * pCount), rIn + 12f, rFull);
            float[] bestRadii = null;
            float bestScore = float.MaxValue;
            for (int rows = 1; rows <= 16; rows++)
            {
                var radii = new float[rows];
                float sum = 0f;
                for (int i = 0; i < rows; i++)
                {
                    radii[i] = rows == 1 ? rOut - 5f : rIn + (rOut - rIn) * i / (rows - 1);
                    sum += radii[i];
                }
                float along = Mathf.PI * sum / pCount;
                float radial = rows == 1 ? rOut - rIn : (rOut - rIn) / (rows - 1);
                float score = Mathf.Abs(Mathf.Log(radial / along));
                if (score < bestScore)
                {
                    bestScore = score;
                    bestRadii = radii;
                    pSize = Mathf.Clamp(Mathf.Min(along, radial) * 0.74f, 3.5f, 9f);
                }
            }
            float total = 0f;
            foreach (float r in bestRadii) total += r;
            var counts = new int[bestRadii.Length];
            var rest = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<float, int>>();
            int left = pCount;
            for (int i = 0; i < bestRadii.Length; i++)
            {
                float raw = pCount * bestRadii[i] / total;
                counts[i] = Mathf.FloorToInt(raw);
                left -= counts[i];
                rest.Add(new System.Collections.Generic.KeyValuePair<float, int>(raw - counts[i], i));
            }
            rest.Sort((a, b) => b.Key.CompareTo(a.Key));
            for (int i = 0; i < rest.Count && left > 0; i++, left--) counts[rest[i].Value]++;
            var pos = new System.Collections.Generic.List<(float t, float r, Vector3 p)>();
            for (int ri = 0; ri < bestRadii.Length; ri++)
            {
                int k = counts[ri];
                float r = bestRadii[ri];
                for (int j = 0; j < k; j++)
                {
                    float t = k == 1 ? Mathf.PI / 2f : Mathf.PI * (1f - (float)j / (k - 1));
                    pos.Add((t, r, new Vector3(cx + r * Mathf.Cos(t), cy - r * Mathf.Sin(t))));
                }
            }
            pos.Sort((a, b) => a.t != b.t ? b.t.CompareTo(a.t) : a.r.CompareTo(b.r));
            foreach (var p in pos) result.Add(p.p);
            return result;
        }
    }
}

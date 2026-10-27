using System;
using System.Collections.Generic;
using NeoModLoader.General;
using UnityEngine;
using UnityEngine.UI;
using static DiplomacyOverhaul.UiKit;

namespace DiplomacyOverhaul
{
    // 议会窗口右边：政策和议案
    public static partial class ParliamentWindow
    {
        static void BuildPolicies(Bill bill)
        {
            // 页签：只显示有政策的
            var tabs = new List<PolicyTab>();
            foreach (PolicyTab t in new[] { PolicyTab.Domestic, PolicyTab.Military, PolicyTab.Diplomacy })
            {
                if (Policies.All.Exists(p => p.tab == t && p.shown(_kingdom))) tabs.Add(t);
            }
            if (!tabs.Contains(_tab) && tabs.Count > 0) _tab = tabs[0];
            float tw = (RightW - 4f * (tabs.Count - 1)) / Mathf.Max(1, tabs.Count);
            for (int i = 0; i < tabs.Count; i++)
            {
                PolicyTab t = tabs[i];
                RectTransform tab = Button(_parent, RightX + i * (tw + 4f), Top + 23f, tw, 16f,
                    t == _tab ? "ui/tab_button_sort_selected" : "ui/tab_button_sort", () =>
                    {
                        _tab = t;
                        Refresh();
                    });
                Text(tab, LM.Get("vd_policy_tab_" + t.ToString().ToLowerInvariant()), 0f, 0f, tw, 16f, 7, Color.white, TextAnchor.MiddleCenter);
            }

            // 政策列表
            var shown = Policies.All.FindAll(p => p.tab == _tab && p.shown(_kingdom));
            float contentH = 0f;
            foreach (Policy p in shown) contentH += (p.id == _open ? 42f : 22f) + 3f;
            RectTransform content = Scroll("scroll_policies", RightX, PolicyY, RightW, PolicyH, contentH);
            float y = 0f;
            foreach (Policy p in shown)
            {
                Policy policy = p;
                bool open = p.id == _open;
                float h = open ? 42f : 22f;
                RectTransform slot = Sliced(content, "ui/special/windowInnerSlicedRound", 0f, y, RightW - 8f, h);
                // 还没有效果的政策灰着，不能点开
                RectTransform head = p.active
                    ? Button(slot, 0f, 0f, RightW - 8f, 22f, null, () =>
                    {
                        _open = _open == policy.id ? null : policy.id;
                        Refresh();
                    })
                    : Box(slot, 0f, 0f, RightW - 8f, 22f);
                RectTransform icon = Icon(head, p.icon, 5f, 4f, 14f);
                if (!p.active) icon.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.4f);
                string policyName = LM.Get(p.id);
                Text(head, policyName, 23f, 0f, 90f, 22f, 7, p.active ? Ink : Muted, TextAnchor.MiddleLeft);
                int current = p.Get(_kingdom);
                string currentName = LM.Get(p.OptionName(_kingdom, current));
                bool locked = Parliament.Locked(_kingdom, p);
                // 写入宪章的政策，名字后面跟卷轴图标和金色小字
                if (locked)
                {
                    float cx = 23f + Measure(policyName, 7) + 3f;
                    UiKit.Picture(head, SpriteTextureLoader.getSprite(CharterIcon), cx, 3.5f, 14f, 15f);
                    Text(head, LM.Get("vd_policy_charter"), cx + 16f, 0f, 40f, 22f, 6, Gold, TextAnchor.MiddleLeft);
                }
                float pillW = Measure(currentName, 6) + 14f;
                RectTransform pill = Sliced(head, p.active ? "ui/special/special_buttonRed" : "ui/special/special_buttonGray",
                    RightW - 30f - pillW, 4f, pillW, 14f);
                if (locked) Charter(pill);
                Text(pill, currentName, 0f, 0f, pillW, 14f, 6, p.active ? Color.white : Muted, TextAnchor.MiddleCenter);
                if (p.active) Text(head, open ? "▲" : "▼", RightW - 24f, 0f, 12f, 22f, 6, Muted, TextAnchor.MiddleCenter);
                if (open)
                {
                    // 锁跟着玩家选中的那一档：这项政策上有玩家的议案就在议案那一档，否则在现行那一档
                    int lockOption = bill != null && bill.player && bill.policy == p ? bill.option : current;
                    float ow = (RightW - 8f - 23f - 6f - 4f * (p.count - 1)) / p.count;
                    for (int o = 0; o < p.count; o++)
                    {
                        int option = o;
                        bool isCurrent = o == current;
                        RectTransform ob = Button(slot, 23f + o * (ow + 4f), 23f, ow, 15f,
                            isCurrent ? "ui/special/special_buttonRed" : "ui/special/special_buttonGray", () => Pick(policy, option));
                        bool lockable = o == lockOption && p.CanPropose(_kingdom);
                        float textW = lockable ? ow - LockW - 7f : ow;
                        Text(ob, LM.Get(p.OptionName(_kingdom, o)), 0f, 0f, textW, 15f, 6, Color.white, TextAnchor.MiddleCenter);
                        if (locked && isCurrent) Charter(ob);
                        if (bill != null && bill.policy == p && bill.option == o) Frame(ob, -1.5f);
                        OptionTip(ob, policy, option);
                        if (lockable) LockButton(ob, policy, option, ow, locked && isCurrent);
                    }
                }
                y += h + 3f;
            }
        }

        const string LockIcon = "ui/Icons/vd_lock";
        const string UnlockIcon = "ui/Icons/vd_unlock";
        const string CharterIcon = "ui/Icons/vd_charter";
        const float LockW = 9f, LockH = 9f;

        // 写入宪章的现行选项换成原版红底金包角按钮（special_buttonRedGodly 裁成长条）。
        // NML 载入的图没有九宫格边距，照原版的像素密度补上
        static Sprite _charter;
        static void Charter(RectTransform pButton)
        {
            if (_charter == null)
            {
                Sprite raw = SpriteTextureLoader.getSprite("ui/special/vd_charter_button");
                Sprite red = SpriteTextureLoader.getSprite("ui/special/special_buttonRed");
                if (raw == null || red == null) return;
                _charter = Sprite.Create(raw.texture, raw.rect, new Vector2(0.5f, 0.5f), red.pixelsPerUnit, 0, SpriteMeshType.FullRect,
                    new Vector4(8f, 8f, 8f, 7f));
            }
            Image img = pButton.GetComponent<Image>();
            img.sprite = _charter;
            img.type = Image.Type.Sliced;
        }

        static void LockIconAt(RectTransform pParent, string pSprite, float pX, float pY) =>
            UiKit.Picture(pParent, SpriteTextureLoader.getSprite(pSprite), pX, pY, LockW, LockH);

        // 选中的那一档右端的锁：锁上就是写入宪章，议会不再提议改这项政策。点议案那一档上的锁，直接颁布再写入
        static void LockButton(RectTransform pOption, Policy pPolicy, int pOptionIndex, float pW, bool pLocked)
        {
            bool enact = pPolicy.Get(_kingdom) != pOptionIndex;
            RectTransform lb = Button(pOption, pW - LockW - 9f, 0f, LockW + 9f, 15f, null, () =>
            {
                if (enact) Parliament.Enact(_kingdom, new Bill { policy = pPolicy, option = pOptionIndex, player = true });
                Parliament.SetLocked(_kingdom, pPolicy, !pLocked);
                Tooltip.hideTooltip();
                Refresh();
            });
            LockIconAt(lb, pLocked ? LockIcon : UnlockIcon, 2f, (15f - LockH) / 2f);
            HoverTip(lb, () => Tooltip.show(lb.gameObject, "tip",
                new TooltipData { tip_name = pLocked ? "vd_policy_unlock" : "vd_policy_lock", tip_description = enact ? "vd_policy_lock_enact_info" : "vd_policy_lock_info" }));
        }

        // 鼠标移上去时显示提示，移开时收起。不用 EventTrigger：它把拖动和滚轮也吃掉，放在滚动列表里列表就滚不动了
        // UiKit 画的色块默认不接射线，透明的悬停区要打开
        static void HoverTip(RectTransform pTarget, Action pShow)
        {
            Graphic graphic = pTarget.GetComponent<Graphic>();
            if (graphic != null) graphic.raycastTarget = true;
            pTarget.gameObject.AddComponent<HoverPointer>().show = pShow;
        }

        // 一股势力为什么这样投：政见、掌权家族顾虑的地方稳定、对提案人的好感，减去维持现状的惯性，合计大于 0 就赞成。都乘 100 取整
        static void VoteTip(RectTransform pTarget, List<ParliamentClan> pClans, Bill pBill, ParliamentClan pClan, ParliamentClan pProposer)
        {
            int current = pBill.policy.Get(_kingdom);
            float stance = Parliament.StanceGain(_kingdom, pClan, pBill.policy, current, pBill.option);
            float stability = pClan.ruling ? -Parliament.UnrestRisk(_kingdom, pClans, pBill.policy, pBill.option) : 0f;
            float favor = Parliament.Favor(pClan, pProposer);
            var lines = new List<(string, string, string)> { WhyLine("vd_vote_why_stance", stance) };
            if (pClan.ruling) lines.Add(WhyLine("vd_vote_why_stability", stability));
            if (pProposer != null && pProposer != pClan) lines.Add(WhyLine("vd_vote_why_favor", favor));
            lines.Add(WhyLine("vd_vote_why_status_quo", -Parliament.Inertia));
            lines.Add(WhyLine("vd_vote_why_total", stance + stability + favor - Parliament.Inertia));
            LM.AddToCurrentLocale(VoteTipName, pClan.clan.name + (pClan.cadet ? " " + LM.Get("vd_parliament_cadet") : ""));
            TipName = VoteTipName;
            TipEffects = new List<(string, string)>();
            TipLines = lines;
            TipBill = null;
            TipVote = null;
            Tooltip.show(pTarget.gameObject, "tip", new TooltipData { tip_name = VoteTipName });
        }

        const string VoteTipName = "vd_vote_why";

        static (string, string, string) WhyLine(string pKey, float pValue)
        {
            int v = Mathf.RoundToInt(pValue * 100f);
            return (pKey, v > 0 ? "+" + v : v.ToString(), v > 0 ? "#8FE07A" : v < 0 ? "#FF8F7A" : null);
        }

        // 共和国的说明里没有王冠，换成国库
        static string DescriptionKey(Policy pPolicy, int pOption)
        {
            string key = pPolicy.OptionKey(pOption) + "_info";
            if (Government.IsRepublic(_kingdom) && LocalizedTextManager.stringExists(key + "_republic")) key += "_republic";
            return key;
        }

        // 点选项：提出议案，顶替原来的议案；点现行选项撤回这项政策上的议案
        static void Pick(Policy pPolicy, int pOption)
        {
            if (!pPolicy.CanPropose(_kingdom)) return;
            Bill old = Parliament.GetBill(_kingdom);
            if (pPolicy.Get(_kingdom) == pOption)
            {
                if (old != null && old.policy == pPolicy) Parliament.ClearBill(_kingdom);
                Refresh();
                return;
            }
            Parliament.SetLocked(_kingdom, pPolicy, false);
            Parliament.SetBill(_kingdom, new Bill { policy = pPolicy, option = pOption, player = true });
            Clan rival = old != null && !old.player ? ProposerClan(old) : null;
            if (rival != null && (old.policy != pPolicy || old.option != pOption))
                WorldTip.showNow(LM.Get("vd_parliament_replaced").Replace("$clan$", rival.name), false, "top");
            Refresh();
        }

        static Clan ProposerClan(Bill pBill)
        {
            if (pBill.proposer < 0) return null;
            Clan clan = World.world.clans.get(ParliamentClan.ClanIdOf(pBill.proposer));
            return clan != null && !clan.isRekt() ? clan : null;
        }

        // 标题右边：哪个氏族提的（玩家提的不写）、离表决还有几年
        static string BillWhen(Bill pBill)
        {
            int years = Mathf.Max(0, Parliament.NextSession(_kingdom) - Date.getCurrentYear());
            string when = LM.Get("vd_parliament_vote_in").Replace("$years$", years.ToString());
            Clan proposer = pBill.player ? null : ProposerClan(pBill);
            return proposer != null ? LM.Get("vd_parliament_proposed_by").Replace("$clan$", proposer.name) + " · " + when : when;
        }

        static void BuildBill(List<ParliamentClan> pClans, Bill pBill, VoteResult result)
        {
            float y = BillY + 23f;
            if (pBill == null)
            {
                RectTransform msg = Sliced(_parent, "ui/special/windowInnerSlicedRound", RightX, y, RightW, 18f);
                Text(msg, LM.Get("vd_parliament_no_bill"), 8f, 0f, RightW - 16f, 18f, 7, Color.white, TextAnchor.MiddleLeft);
                Text(_parent, LM.Get("vd_parliament_no_bill_hint"), RightX + 3f, y + 21f, RightW - 6f, 10f, 6, Muted, TextAnchor.MiddleLeft);
                return;
            }

            RectTransform title = Sliced(_parent, "ui/special/windowInnerSlicedRound", RightX, y, RightW, 18f);
            string titleText = LM.Get(pBill.policy.id) + LM.Get("vd_parliament_colon") + LM.Get(pBill.policy.OptionName(_kingdom, pBill.policy.Get(_kingdom)))
                               + " → " + LM.Get(pBill.policy.OptionName(_kingdom, pBill.option));
            Text(title, titleText, 7f, 0f, RightW - 14f, 18f, 7, Ink, TextAnchor.MiddleLeft);
            _billWhen = Text(title, BillWhen(pBill), 7f, 0f, RightW - 14f, 18f, 6, Muted, TextAnchor.MiddleRight).GetComponent<Text>();
            y += 20f;

            RectTransform bar = Sliced(_parent, "ui/special/windowInnerSlicedRound", RightX, y, RightW, 16f);
            float inner = RightW - 6f;
            float yesW = result.Total > 0 ? inner * result.yes / result.Total : 0f;
            Rect(bar, 3f, 3f, yesW, 10f, YesBar);
            Rect(bar, 3f + yesW, 3f, inner - yesW, 10f, NoBar);
            Rect(bar, 3f + inner * pBill.policy.threshold - 0.5f, 1f, 1f, 14f, Ink);
            Text(bar, LM.Get("vd_parliament_yes") + " " + result.yes, 7f, 0f, 80f, 16f, 6, Color.white, TextAnchor.MiddleLeft);
            Text(bar, LM.Get("vd_parliament_no") + " " + result.no, RightW - 87f, 0f, 80f, 16f, 6, Color.white, TextAnchor.MiddleRight);
            y += 18f;

            List<ParliamentClan> list = RulingFirst(pClans);
            ParliamentClan proposer = Parliament.Proposer(pClans, pBill);
            const float rowH = 11f;
            // 按钮贴着窗口底部，名单占满中间
            float buttonsY = H - 30f;
            RectTransform votes = Scroll("scroll_votes", RightX, y, RightW, buttonsY - 3f - y, list.Count * rowH);
            float ry = 0f;
            foreach (ParliamentClan pc in list)
            {
                bool yes = result.votes.TryGetValue(pc.Key, out bool v) && v;
                Swatch(votes, 6f, ry + 3f, 5f, FactionColor(pc.faction), 1f);
                string suffix = pc.cadet ? " " + LM.Get("vd_parliament_cadet") : "";
                string name = UiKit.Fit(pc.clan.name, 6, RightW - 102f - Measure(suffix, 6)) + suffix;
                Text(votes, name, 16f, ry, RightW - 102f, rowH, 6, Ink, TextAnchor.MiddleLeft);
                Text(votes, pc.seats.ToString(), RightW - 82f, ry, 28f, rowH, 6, Gold, TextAnchor.MiddleRight);
                Icon(votes, yes ? "ui/Icons/iconMoodHappy" : "ui/Icons/iconMoodAngry", RightW - 50f, ry + 1.5f, 8f);
                Text(votes, LM.Get(yes ? "vd_parliament_yes" : "vd_parliament_no"), RightW - 40f, ry, 30f, rowH, 6, yes ? YesText : NoText, TextAnchor.MiddleRight);
                ParliamentClan clan = pc;
                RectTransform hover = UiKit.Rect(votes, 0f, ry, RightW - 8f, rowH, new Color(0f, 0f, 0f, 0f));
                HoverTip(hover, () => VoteTip(hover, pClans, pBill, clan, proposer));
                ry += rowH;
            }
            y = buttonsY;

            float bw = (RightW - 50f - 8f) / 2f;
            RectTransform vote = Button(_parent, RightX, y, bw, 18f, "ui/special/special_buttonRed", () =>
            {
                Bill b = Parliament.GetBill(_kingdom);
                if (b != null) Parliament.VoteNow(_kingdom, b);
                Refresh();
            });
            Text(vote, LM.Get("vd_parliament_submit"), 0f, 0f, bw, 18f, 7, Color.white, TextAnchor.MiddleCenter);
            RectTransform force = Button(_parent, RightX + bw + 4f, y, bw, 18f, "ui/special/special_buttonGray", () =>
            {
                Bill b = Parliament.GetBill(_kingdom);
                if (b != null) Parliament.Enact(_kingdom, b);
                Refresh();
            });
            Text(force, LM.Get("vd_parliament_force"), 0f, 0f, bw, 18f, 7, Color.white, TextAnchor.MiddleCenter);
            RectTransform withdraw = Button(_parent, RightX + 2f * bw + 8f, y, 50f, 18f, "ui/special/special_buttonGray", () =>
            {
                Parliament.ClearBill(_kingdom);
                Refresh();
            });
            Text(withdraw, LM.Get("vd_parliament_withdraw"), 0f, 0f, 50f, 18f, 7, Color.white, TextAnchor.MiddleCenter);
        }
    }
}

using System.Collections.Generic;
using NeoModLoader.General;
using UnityEngine;

namespace DiplomacyOverhaul
{
    // 一个集团：议会里几股势力结成的同盟。魁首是议席最多的成员，集团名取魁首的城
    public class Bloc
    {
        public int id;
        public readonly List<ParliamentClan> members = new List<ParliamentClan>();
        // 成员在建这个集团时那份议会列表里的下标，和 members 一一对应
        public readonly List<int> at = new List<int>();
        public ParliamentClan leader;
        public int seats;
        // 集团的立场：成员的意识形态按议席加权取平均
        public float x, y;
        public Faction faction;

        // 议会窗口里选中集团用的 key，和势力的 Key 分开
        public const long KeyBase = 1L << 50;
        public long Key => KeyBase + id;

        public bool Has(ParliamentClan pClan)
        {
            foreach (ParliamentClan m in members)
                if (m.Key == pClan.Key) return true;
            return false;
        }
    }

    // 集团：一群互相抱团的家族，对外一致行动，各家靠对集团的好感留在里面。
    // 对集团的好感是对其他成员好感的议席加权平均。进集团靠拉拢（见 Blocs.Plots），出集团靠脱离集团的密谋；
    // 表决时集团按成员合计的得失表态，成员都跟着投，被裹挟吃了亏的对魁首记积怨
    public static partial class Blocs
    {
        const string KeyList = "vd_blocs";
        const string KeyNext = "vd_bloc_next";

        // 对集团（或对发起人）的好感过了这条线才肯入伙；跌破退出线就想走；跳槽要比现在的集团高出这么多
        public const int JoinLine = 40, LeaveLine = 0, SwitchMargin = 15;

        // ---------- 存取 ----------

        // kingdom.data 里存成「编号:成员,成员;…」，成员是 ParliamentClan.Key
        static List<(int id, List<long> keys)> Load(Kingdom pKingdom)
        {
            var list = new List<(int, List<long>)>();
            pKingdom.data.get(KeyList, out string raw, null);
            if (string.IsNullOrEmpty(raw)) return list;
            foreach (string item in raw.Split(';'))
            {
                int colon = item.IndexOf(':');
                if (colon <= 0 || !int.TryParse(item.Substring(0, colon), out int id)) continue;
                var keys = new List<long>();
                foreach (string s in item.Substring(colon + 1).Split(','))
                    if (long.TryParse(s, out long key) && !keys.Contains(key)) keys.Add(key);
                list.Add((id, keys));
            }
            return list;
        }

        // 只剩一家的集团不存
        static void Save(Kingdom pKingdom, List<(int id, List<long> keys)> pList)
        {
            var parts = new List<string>();
            foreach (var (id, keys) in pList)
                if (keys.Count >= 2) parts.Add(id + ":" + string.Join(",", keys));
            if (parts.Count > 0) pKingdom.data.set(KeyList, string.Join(";", parts));
            else pKingdom.data.removeString(KeyList);
        }

        public static bool Any(Kingdom pKingdom) => !string.IsNullOrEmpty(Raw(pKingdom));

        // 存档里的原样字符串：界面拿它当数据指纹
        public static string Raw(Kingdom pKingdom)
        {
            pKingdom.data.get(KeyList, out string raw, null);
            return raw ?? "";
        }

        // 存档里的成员换成这一刻议会里的势力；不在议会里的成员不算，凑不满两家的集团不算
        public static List<Bloc> Of(Kingdom pKingdom, List<ParliamentClan> pClans)
        {
            var result = new List<Bloc>();
            if (!Any(pKingdom)) return result;
            foreach (var (id, keys) in Load(pKingdom))
            {
                var b = new Bloc { id = id };
                foreach (long key in keys)
                {
                    ParliamentClan pc = pClans.Find(c => c.Key == key);
                    if (pc != null) b.members.Add(pc);
                }
                if (b.members.Count < 2) continue;
                Finish(b);
                foreach (ParliamentClan m in b.members) b.at.Add(pClans.IndexOf(m));
                result.Add(b);
            }
            return result;
        }

        static void Finish(Bloc pBloc)
        {
            float x = 0f, y = 0f;
            foreach (ParliamentClan m in pBloc.members)
            {
                pBloc.seats += m.seats;
                x += m.seats * m.x;
                y += m.seats * m.y;
                if (pBloc.leader == null || m.seats > pBloc.leader.seats) pBloc.leader = m;
            }
            if (pBloc.seats > 0)
            {
                pBloc.x = x / pBloc.seats;
                pBloc.y = y / pBloc.seats;
            }
            pBloc.faction = Parliament.FactionOf(pBloc.x, pBloc.y);
            // 集团里按派系从激进到保守、同派系按议席排：半圆和列表里同派系的成员坐在一起
            pBloc.members.Sort((a, b) =>
            {
                int fa = Parliament.FactionOrder(a.faction), fb = Parliament.FactionOrder(b.faction);
                return fa != fb ? fa.CompareTo(fb) : b.seats.CompareTo(a.seats);
            });
        }

        public static Bloc Find(List<Bloc> pBlocs, ParliamentClan pClan)
        {
            if (pClan == null) return null;
            foreach (Bloc b in pBlocs)
                if (b.Has(pClan)) return b;
            return null;
        }

        // 魁首的城：王室按首都，其他按议席最多的那座城
        public static City Home(ParliamentClan pClan)
        {
            City best = null;
            foreach (City c in pClan.cities)
            {
                if (pClan.ruling && c.isCapitalCity()) return c;
                if (best == null || Parliament.CitySeats(c) > Parliament.CitySeats(best)) best = c;
            }
            return best;
        }

        public static string Name(Bloc pBloc)
        {
            City home = Home(pBloc.leader);
            return LM.Get("vd_bloc_name").Replace("$city$", home != null ? home.data.name : pBloc.leader.clan.name);
        }

        // 对集团的好感：对其他成员的好感按议席加权取平均
        public static float Affinity(ParliamentClan pClan, Bloc pBloc)
        {
            float sum = 0f;
            int seats = 0;
            foreach (ParliamentClan m in pBloc.members)
            {
                if (m.Key == pClan.Key) continue;
                sum += m.seats * Relations.Value(pClan, m);
                seats += m.seats;
            }
            return seats > 0 ? sum / seats : 0f;
        }

        // ---------- 成员变动 ----------

        static void Remove(List<(int id, List<long> keys)> pList, long pKey)
        {
            foreach (var (_, keys) in pList) keys.Remove(pKey);
        }

        // 两家结成新集团；对方原来在别的集团就从那里出来
        public static int Found(Kingdom pKingdom, ParliamentClan pFounder, ParliamentClan pPartner)
        {
            var list = Load(pKingdom);
            Remove(list, pFounder.Key);
            Remove(list, pPartner.Key);
            pKingdom.data.get(KeyNext, out int id, 1);
            pKingdom.data.set(KeyNext, id + 1);
            list.Add((id, new List<long> { pFounder.Key, pPartner.Key }));
            Save(pKingdom, list);
            return id;
        }

        public static void Join(Kingdom pKingdom, Bloc pBloc, ParliamentClan pClan)
        {
            var list = Load(pKingdom);
            Remove(list, pClan.Key);
            foreach (var (id, keys) in list)
                if (id == pBloc.id) keys.Add(pClan.Key);
            Save(pKingdom, list);
        }

        public static void Leave(Kingdom pKingdom, ParliamentClan pClan)
        {
            var list = Load(pKingdom);
            Remove(list, pClan.Key);
            Save(pKingdom, list);
        }

        // 每年顺手清掉已经不在议会里的成员，凑不满两家的集团就此解散
        public static void Clean(Kingdom pKingdom, List<ParliamentClan> pClans)
        {
            if (!Any(pKingdom)) return;
            var list = Load(pKingdom);
            foreach (var (_, keys) in list) keys.RemoveAll(key => pClans.Find(c => c.Key == key) == null);
            Save(pKingdom, list);
        }

        // ---------- 表决 ----------

        // 各股势力先按自己的分数（得失加上对提案人的好感）表态；集团按成员议席加权的分数合计表态，成员都跟着投。
        // 提案人所在的集团全体赞成。pBlocs 要按 pClans 建（见 Of）
        public static bool[] Decide(List<ParliamentClan> pClans, List<Bloc> pBlocs, float[] pScores, ParliamentClan pProposer, float pInertia)
        {
            var yes = new bool[pClans.Count];
            for (int i = 0; i < pClans.Count; i++) yes[i] = pScores[i] > pInertia;
            foreach (Bloc b in pBlocs)
            {
                bool by;
                if (pProposer != null && b.Has(pProposer)) by = true;
                else
                {
                    float sum = 0f;
                    for (int k = 0; k < b.members.Count; k++) sum += b.members[k].seats * pScores[b.at[k]];
                    by = b.seats > 0 && sum / b.seats > pInertia;
                }
                foreach (int i in b.at) yes[i] = by;
            }
            return yes;
        }

        // 被裹挟：最低也算吃了这么多亏
        const float MinDragged = 0.05f;

        // 表决之后：集团这一边赢了时，自己本来不想这么投的成员按吃的亏对魁首记积怨，本来就想这么投的按得的好处记恩情。
        // 魁首自己提的议案照提案人的规则记（见 Relations.AfterVote），这里不重复
        public static void AfterVote(List<ParliamentClan> pClans, List<Bloc> pBlocs, ParliamentClan pProposer, float[] pGains,
            bool[] pOwn, bool[] pFinal, bool pPassed)
        {
            foreach (Bloc b in pBlocs)
            {
                if (pProposer != null && b.leader.Key == pProposer.Key) continue;
                bool by = pFinal[pClans.IndexOf(b.leader)];
                if (by != pPassed) continue;
                foreach (ParliamentClan m in b.members)
                {
                    if (m == b.leader) continue;
                    int i = pClans.IndexOf(m);
                    // 议案通过，得失就是这一档带来的；被否决，得失反过来
                    float benefit = pPassed ? pGains[i] : -pGains[i];
                    if (pOwn[i] != by) Relations.Resent(m, b.leader, Mathf.Max(MinDragged, -benefit));
                    else if (benefit > 0f) Relations.Thank(m, b.leader, benefit);
                }
            }
        }
    }
}

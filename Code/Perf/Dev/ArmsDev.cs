using System.Collections.Generic;
using System.Linq;

namespace DiplomacyOverhaul.Perf
{
    // 开发命令 arms：城市最多的几个国家的军械拨款、制式、国库、可收购的金属，禁军每个部位的材质分布和弓手人数。
    // arms run 立即结算一次军械（不等年度），前后各打印一次
    static class ArmsDev
    {
        static readonly EquipmentType[] Slots = { EquipmentType.Weapon, EquipmentType.Helmet, EquipmentType.Armor, EquipmentType.Boots };

        public static void Run(string[] a)
        {
            if (a.Length > 1 && a[1] == "storage")
            {
                // arms storage <国家>：这个国家每座城的禁军、地方兵人数和仓库里各类装备的件数
                Kingdom target = World.world.kingdoms.FirstOrDefault(k => k.isAlive() && k.name == a[2]);
                if (target == null) { Dev.Log("  ! kingdom not found"); return; }
                foreach (City c in target.getCities())
                {
                    int guard = c.getUnits().Count(u => u.isWarrior() && Guard.IsMember(u));
                    int local = c.getUnits().Count(u => u.isWarrior() && !Guard.IsMember(u));
                    string items = string.Join(" ", Slots.Select(t => t + "=" + c.data.equipment.getEquipmentList(t).Count));
                    var locals = Armory.LocalMembers(c);
                    int score = locals.Sum(u => Slots.Sum(t => u.equipment.getSlot(t).getItem()?.getAsset().equipment_value ?? 0));
                    int cityMade = locals.Sum(u => Slots.Count(t => Armory.IsLocal(u.equipment.getSlot(t).getItem())));
                    Dev.Log($"  {c.name}{(c.isCapitalCity() ? "*" : "")} gold={c.getResourcesAmount("gold")} tier={Armory.TierOf(c)} guard={guard} local={local}"
                        + $" local_gear_avg={(locals.Count > 0 ? (float)score / locals.Count : 0):F1} city_made={cityMade} {items}");
                }
                return;
            }
            bool run = a.Length > 1 && a[1] == "run";
            int count = a.Length > 1 && !run ? int.Parse(a[1]) : 5;
            var kingdoms = World.world.kingdoms.Where(k => k.isAlive() && k.isCiv()).OrderByDescending(k => k.countCities()).Take(count).ToList();
            Print(kingdoms);
            if (!run) return;
            Armory.UpdateAll();
            Dev.Log("  -- after run --");
            Print(kingdoms);
        }

        static void Print(List<Kingdom> pKingdoms)
        {
            Dev.Log($"  year {World.world.getCurWorldTime() / Clock.YearLength:F1}");
            foreach (Kingdom k in pKingdoms)
            {
                List<Actor> members = Armory.Members(k);
                Dev.Log($"  {k.data.name} arms={Policies.Value(k, Policies.Arms)} tier={Armory.TierOf(k)} treasury={Treasury.Get(k)} guard={members.Count} storage={k.getCities().Sum(c => c.data.equipment.countItems())}"
                    + $" metals={Armory.Stock(k, "common_metals")} mythril={Armory.Stock(k, "mythril")} adamantine={Armory.Stock(k, "adamantine")}");
                var raw = new List<string>();
                foreach (ResourceAsset r in AssetManager.resources.strategic_resource_assets)
                    raw.Add(r.id + "=" + k.getCities().Sum(c => c.getResourcesAmount(r.id)));
                Dev.Log("    stock " + string.Join(" ", raw));
                int archers = 0, official = 0, mods = 0, civil = 0, civilMods = 0, damaged = 0;
                var slots = new List<string>();
                foreach (EquipmentType slot in Slots)
                {
                    var dist = new SortedDictionary<int, int>();
                    foreach (Actor m in members)
                    {
                        Item item = m.equipment.getSlot(slot).getItem();
                        int v = item?.getAsset().equipment_value ?? 0;
                        dist[v] = dist.TryGetValue(v, out int n) ? n + 1 : 1;
                        if (item == null) continue;
                        if (Armory.IsOfficial(item)) { official++; mods += item.data.modifiers.Count; if (item.needRepair()) damaged++; }
                        else { civil++; civilMods += item.data.modifiers.Count; }
                    }
                    slots.Add(slot + "{" + string.Join(" ", dist.Select(p => p.Key + ":" + p.Value)) + "}");
                }
                foreach (Actor m in members)
                {
                    m.data.get(Armory.KeyRole, out int role, 0);
                    if (role == 2) archers++;
                }
                Dev.Log($"    {string.Join(" ", slots)} archers={archers} official_items={official} damaged={damaged} mods/official={(official > 0 ? (float)mods / official : 0):F2} mods/other={(civil > 0 ? (float)civilMods / civil : 0):F2}");
            }
        }
    }
}

"""天命模型的国家层面仿真。

不模拟单位，只模拟每个大国每年的几个数：城市数、原版忠诚为正的城市占比、国王能力、
附庸整合度、无家可归比例、战争。事件频率取自 tamriel_910 存档 910–974 年的采样
（bin/dev/mandate*.csv），原版忠诚和整合度的规则照抄游戏代码。

用法：python tools/sim/mandate.py [--seeds N] [--plot out.png]
"""
import argparse
import math
import random
from dataclasses import dataclass, field

# ---------------- 天命模型（要定的就是这一段） ----------------

@dataclass
class Model:
    base: float = 84.0         # 目标值的底数
    pull: float = 0.10         # 每年向目标值靠拢的比例
    aging: float = 100.0       # 王朝老化：目标值减 积弊² / aging，早年几乎不老，晚年积重难返
    decay_scale: float = 25.0  # 积弊每年 +1 - 治理/decay_scale（治理 +10 → 0.6，-10 → 1.4）
    decay_min: float = 0.3
    decay_max: float = 2.0
    w_loyal: float = 40.0      # 忠诚城市占比，以 0.85（普通大国）为中点
    w_king: float = 0.6        # 国王管理+智力每高于平均 1 点
    king_cap: float = 10.0
    no_king: float = -10.0
    w_vassal: float = 0.2      # 附庸平均整合度每高于 50 一点
    w_people: float = 40.0     # 无家可归比例每低于 0.33 一个单位
    people_cap: float = 5.0
    # 一次性事件，直接加在天命上
    war_won: float = 5.0
    war_lost: float = -10.0
    city_lost: float = -2.0
    city_gained: float = 1.0
    capital_lost: float = -15.0
    # 阶段门槛
    start: float = 60.0        # 新王朝起点
    prime_at: float = 75.0     # 涨到这里进入鼎盛（衰落中涨到这里 = 中兴）
    decline_below: float = 50.0
    stillborn_below: float = 40.0  # 新生期就跌到这里，直接衰落
    renewal_max: int = 30      # 新生最多这么多年，之后没跌破衰落线就算进入鼎盛
    rebellion_below: float = 20.0
    # 中兴密谋：成功时积弊 -restore_age，天命 +restore_m 且至少回到鼎盛线
    restore_m: float = 15.0
    restore_age: float = 40.0

    small_lord: float = 5.0    # 尾大不掉：带着附庸、自己不到 6 座城的宗主，每少一座城治理减多少

    def target(self, k):
        return self.base + self.governance(k) - k.decay * k.decay / self.aging

    def decay_rate(self, k):
        return max(self.decay_min, min(self.decay_max, 1 - self.governance(k) / self.decay_scale))

    def governance(self, k):
        t = self.w_loyal * (k.loyal_share() - 0.85)
        if k.king is None:
            t += self.no_king
        else:
            t += max(-self.king_cap, min(self.king_cap, self.w_king * (k.king - 29)))
        if k.vassals:
            t += self.w_vassal * (k.integration - 50)
        t += max(-self.people_cap, min(self.people_cap, self.w_people * (0.33 - k.homeless)))
        if k.vassals and k.cities < EMPIRE:
            t -= self.small_lord * (EMPIRE - k.cities)
        return t


# ---------------- 世界（事件频率来自采样） ----------------

@dataclass
class World:
    war_start: float = 0.02    # 不在打仗时每年开战的概率（采样：19% 的年份在打仗）
    war_min: int = 5           # 投降最早开战 5 年，原版议和最早 10 年
    war_end: float = 0.12      # 满 war_min 后每年结束的概率（平均约 12 年）
    crisis_min: int = 5        # 大叛乱引发的仗同样受这些限制
    crisis_end: float = 0.12
    war_win: float = 0.40      # 结束时：赢 / 输 / 其余议和
    war_lose: float = 0.30
    war_city_lost: float = 0.15   # 打仗的年份丢一座城 / 得一座城的概率
    war_city_gained: float = 0.12
    capital_share: float = 0.10   # 丢的城是首都的概率
    peace_gain: float = 0.02      # 太平年份扩张一座城
    king_change: float = 0.05     # 采样：每年 4–6%
    interregnum: float = 0.3      # 换王时空位一年的概率
    revolt_rate: float = 0.006    # 每座不忠的城每年起事的概率（原版叛乱密谋）
    restore_try: float = 0.08     # 衰落期每年发起中兴密谋并做完的概率
    restore_ok: float = 0.30


EMPIRE = 6  # 有周期：自己 6 座城以上，或者有附庸（且自己不是附庸）
PRIME_LOYALTY = 10  # 鼎盛期的周期修正，换算成「不忠城市占比」的变化见 Kingdom.disloyal_cities
DECLINE_LOYALTY = 8.0  # 衰落期每低于 50 一点天命扣的忠诚
LOYALTY_LOW, LOYALTY_HIGH = 150, 270  # 大国非首都城市的原版忠诚大致在这个范围（游戏里取样）


@dataclass
class Kingdom:
    rng: random.Random
    w: World
    cities: int = 8
    max_cities: int = 6        # 原版 civ_base_cities + 国王的 cities 属性
    quality: float = 0.9       # 不超编时忠诚城市的占比（文化、宗教、距离等固定因素）
    king: float | None = 29.0
    vassals: int = 0
    integ_bias: float = 0.0    # 附庸的结构因素（接壤、同文化……）每年合计
    integration: float = 50.0
    homeless: float = 0.33
    age: int = 0
    decay: float = 0.0
    m: float = 60.0
    phase: str = "renewal"
    decline_years: int = 0
    at_war: bool = False
    war_years: int = 0
    crisis: int = 0
    log: list = field(default_factory=list)

    def loyal_share(self):
        over = max(0, self.cities - self.max_cities)
        return max(0.0, min(1.0, self.quality - 0.04 * over))

    def disloyal_cities(self):
        """真正会起事的城：原版忠诚加上周期修正（衰落期按天命扣，忠诚按均匀分布换算成不忠的城）。"""
        share = 1 - self.loyal_share()
        if self.phase == "decline":
            cut = DECLINE_LOYALTY * max(0.0, 50 - self.m)
            share += self.loyal_share() * max(0.0, min(1.0, (cut - LOYALTY_LOW) / (LOYALTY_HIGH - LOYALTY_LOW)))
        elif self.phase == "prime":
            share -= 0.1
        share = max(0.0, min(1.0, share))
        return share * max(0, self.cities - 1)

    def new_king(self):
        r = self.rng
        self.king = None if r.random() < self.w.interregnum else max(14.0, r.gauss(29, 9))
        self.max_cities = r.randint(5, 10)


def step(k: Kingdom, mdl: Model):
    r, w = k.rng, k.w
    shock = 0.0
    events = []

    # 国王
    if k.king is None or r.random() < w.king_change:
        k.new_king()

    # 战争
    if k.at_war:
        if r.random() < w.war_city_lost:
            k.cities -= 1
            shock += mdl.city_lost
            if r.random() < w.capital_share:
                shock += mdl.capital_lost
                events.append("capital")
        if r.random() < w.war_city_gained:
            k.cities += 1
            shock += mdl.city_gained
        k.war_years += 1
        if k.war_years >= w.war_min and r.random() < w.war_end:
            k.at_war = False
            x = r.random()
            if x < w.war_win:
                shock += mdl.war_won
                k.integration += 5
            elif x < w.war_win + w.war_lose:
                shock += mdl.war_lost
                k.integration -= 5
    else:
        if r.random() < w.war_start:
            k.at_war, k.war_years = True, 0
        if r.random() < w.peace_gain:
            k.cities += 1
            shock += mdl.city_gained

    # 原版叛乱：不忠的城零星起事，每次带走一半不忠的城
    u = k.disloyal_cities()
    if u >= 1 and r.random() < min(0.9, w.revolt_rate * u):
        lost = max(1, int(u / 2))
        k.cities -= lost
        shock += mdl.city_lost * lost
        events.append("revolt")

    # 附庸整合度：宗主阶段决定方向（现有规则）
    if k.vassals:
        phase_d = {"renewal": 1, "prime": 1, "decline": -3, "collapse": -4}[k.phase]
        k.integration = max(0, min(100, k.integration + phase_d + k.integ_bias))

    k.homeless = max(0.1, min(0.6, k.homeless + r.gauss(0, 0.03) + 0.1 * (0.33 - k.homeless)))

    # 天命
    if k.phase == "collapse":
        k.crisis += 1
        if k.crisis >= w.crisis_min and r.random() < w.crisis_end:
            return "renewal" if k.cities >= EMPIRE or k.vassals else "end"
        return None
    k.age += 1
    k.decay += mdl.decay_rate(k)
    k.m += mdl.pull * (mdl.target(k) - k.m) + shock
    if k.phase == "decline":
        k.decline_years += 1
        if r.random() < w.restore_try and r.random() < w.restore_ok:
            k.m = max(k.m + mdl.restore_m, mdl.prime_at)
            k.decay = max(0.0, k.decay - mdl.restore_age)
            events.append("restore")
    k.m = max(0.0, min(100.0, k.m))

    if k.cities < EMPIRE and not k.vassals:
        return "end"
    if k.phase == "renewal":
        if k.m >= mdl.prime_at or k.age >= mdl.renewal_max and k.m >= mdl.decline_below:
            return "prime"
        if k.m < mdl.stillborn_below or k.age >= mdl.renewal_max:
            return "decline"
    elif k.phase == "prime":
        if k.m < mdl.decline_below:
            return "decline"
    elif k.phase == "decline":
        if k.m >= mdl.prime_at:
            return "prime"
        if k.m < mdl.rebellion_below or "capital" in events and k.m < mdl.decline_below:
            return "rebellion"
    return None


def great_rebellion(k: Kingdom):
    r = k.rng
    k.cities -= int(0.6 * (k.cities - 1))
    k.integration = max(0, k.integration - 20)
    k.crisis = 0
    k.phase = "collapse"


def new_kingdom(rng, w, mdl, initial=False):
    k = Kingdom(rng=rng, w=w)
    k.cities = rng.randint(6, 14)
    k.quality = rng.uniform(0.75, 1.0)
    k.new_king()
    if k.king is None:
        k.king = max(14.0, rng.gauss(29, 9))
    k.vassals = rng.random() < 0.5 and rng.randint(1, 8) or 0
    k.integ_bias = rng.choice([-2, -1, 0, 1, 2])
    k.integration = rng.uniform(30, 90)
    if initial:
        # 读档时已有的大国：算鼎盛，年龄随机
        k.phase = "prime"
        k.age = rng.randint(0, 60)
        k.decay = float(k.age)
        k.m = max(mdl.prime_at, min(100, mdl.target(k)))
    else:
        k.m = mdl.start
    return k


def run(mdl, w, seed, years=600, initial=False):
    """跑一个国家 years 年，记录每个阶段的起止。崩溃后挺过来算新王朝。"""
    rng = random.Random(seed)
    k = new_kingdom(rng, w, mdl, initial)
    spans = []  # (phase, start_year, end_year)
    dynasties = []  # 每个王朝：各阶段年数、结局
    cur = {"renewal": 0, "prime": 0, "decline": 0, "restores": 0, "first_decline": None}
    since = 0
    trace = []
    for y in range(years):
        nxt = step(k, mdl)
        trace.append((y, k.m, k.phase, k.cities))
        if k.phase in cur:
            cur[k.phase] += 1
        if nxt is None:
            continue
        spans.append((k.phase, since, y))
        since = y
        if nxt == "rebellion":
            if cur["first_decline"] is None:
                cur["first_decline"] = cur["renewal"] + cur["prime"]
            dynasties.append(dict(cur, end="rebellion", length=cur["renewal"] + cur["prime"] + cur["decline"]))
            great_rebellion(k)
            continue
        if nxt == "end":
            dynasties.append(dict(cur, end="shrank", length=cur["renewal"] + cur["prime"] + cur["decline"]))
            break
        if nxt == "renewal":
            k.phase, k.age, k.decay, k.m, k.decline_years = "renewal", 0, 0.0, mdl.start, 0
            cur = {"renewal": 0, "prime": 0, "decline": 0, "restores": 0, "first_decline": None}
            continue
        if nxt == "decline":
            k.decline_years = 0
            if cur["first_decline"] is None:
                cur["first_decline"] = cur["renewal"] + cur["prime"]
        if nxt == "prime" and k.phase == "decline":
            cur["restores"] += 1
        k.phase = nxt
    return dynasties, trace


def pct(xs, q):
    xs = sorted(xs)
    if not xs:
        return float("nan")
    return xs[min(len(xs) - 1, int(q * len(xs)))]


def summarize(label, mdl, w, seeds, initial=False):
    ds = []
    for s in range(seeds):
        d, _ = run(mdl, w, s, initial=initial)
        ds.extend(x for x in d if not initial or x is not d[0])
    reb = [d for d in ds if d["end"] == "rebellion"]
    fmt = lambda xs: f"{pct(xs, .1):4.0f}/{pct(xs, .5):4.0f}/{pct(xs, .9):4.0f}"
    print(f"{label:14s} 王朝 {len(ds):5d}  大叛乱收场 {len(reb) / max(1, len(ds)):4.0%}  "
          f"新生 {fmt([d['renewal'] for d in reb])}  鼎盛 {fmt([d['prime'] for d in reb])}  "
          f"衰落 {fmt([d['decline'] for d in reb])}  全程 {fmt([d['length'] for d in reb])}  "
          f"中兴 {sum(d['restores'] for d in reb) / max(1, len(reb)):.2f}")
    return ds


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--seeds", type=int, default=2000)
    ap.add_argument("--plot")
    ap.add_argument("--decline-loyalty", type=float, default=DECLINE_LOYALTY)
    a = ap.parse_args()
    globals()["DECLINE_LOYALTY"] = a.decline_loyalty
    mdl = Model()
    print("各阶段年数：10%/50%/90% 分位，只统计以大叛乱收场的王朝")
    for name, scale in [("太平", 0.4), ("平常", 1.0), ("多战", 2.0), ("乱世", 3.5)]:
        w = World(war_start=World.war_start * scale)
        summarize(name, mdl, w, a.seeds)
    if a.plot:
        plot(mdl, a.plot)


def plot(mdl, out):
    import matplotlib
    matplotlib.use("Agg")
    import matplotlib.pyplot as plt
    plt.rcParams["font.family"] = ["PingFang SC", "Heiti SC", "sans-serif"]
    fig, axes = plt.subplots(4, 1, figsize=(11, 10), sharex=True)
    colors = {"renewal": "#8FE07A", "prime": "#FFD24A", "decline": "#C9A27E", "collapse": "#FF5A4A"}
    for ax, seed in zip(axes, [1, 2, 3, 4]):
        _, tr = run(mdl, World(), seed, years=400)
        ys = [t[0] for t in tr]
        ax.plot(ys, [t[1] for t in tr], color="k", lw=1)
        for y, _, ph, _ in tr:
            ax.axvspan(y, y + 1, color=colors[ph], alpha=0.35, lw=0)
        for v in (mdl.prime_at, mdl.decline_below, mdl.rebellion_below):
            ax.axhline(v, color="grey", lw=0.5, ls="--")
        ax.set_ylim(0, 100)
        ax.set_ylabel(f"种子 {seed}")
    axes[-1].set_xlabel("年")
    fig.suptitle("天命值（黑线）与阶段：绿 新生 / 黄 鼎盛 / 褐 衰落 / 红 崩溃")
    fig.tight_layout()
    fig.savefig(out, dpi=110)


if __name__ == "__main__":
    main()

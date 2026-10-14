"""小宗主：自己不到 6 座城、靠附庸才有周期。看多少年衰落、附庸多少年走完。

附庸逐个模拟：整合度按宗主阶段和「每少一座城 -1」变化；好感 = 效忠 30 + 宗主国运
+ 宗主势微，为负时每年按 LEAVE 的概率独立成功（这个概率是估计值，没有采样依据）；
大叛乱时按 (90 - 整合度)% 造反。

用法：python tools/sim/small_lord.py [天命每城] [好感每城] [整合度每城每年]，默认 5 10 1
"""
import sys, random
sys.path.insert(0, 'tools/sim')
import mandate as M

LEAVE = 0.08   # 好感为负的附庸每年发起独立并成功的概率
OP_PEN = 10.0  # 宗主势微：每少一座城附庸好感 -OP_PEN
INT_PEN = 1.0  # 每少一座城整合度每年额外 -INT_PEN

def run(seed, own, nv, initial, mdl, w, years=200):
    rng = random.Random(seed)
    k = M.new_kingdom(rng, w, mdl)
    k.cities = own
    vl = [rng.uniform(30, 90) for _ in range(nv)]
    k.vassals, k.integration = nv, sum(vl) / nv
    if initial:
        k.phase = 'prime'; k.decay = float(rng.randint(0, 60))
        k.m = max(mdl.prime_at, min(100, mdl.target(k)))
    first_decline = None; grew = False
    for y in range(years):
        nxt = M.step(k, mdl)
        k.cities = max(0, k.cities)
        grew |= k.cities >= 6
        dy = k.decline_years if k.phase == 'decline' else 0
        opinion = 30 + {'renewal': 10, 'prime': 20, 'decline': -min(90, 10 + 3 * dy), 'collapse': 0}[k.phase]
        miss = max(0, 6 - k.cities)
        opinion -= OP_PEN * miss
        drift = {'renewal': 1, 'prime': 1, 'decline': -3, 'collapse': -4}[k.phase] + k.integ_bias - INT_PEN * miss
        vl = [max(0, min(100, i + drift)) for i in vl if not (opinion < 0 and rng.random() < LEAVE)]
        if nxt == 'rebellion':
            vl = [i for i in vl if rng.random() >= max(10, min(90, 90 - i)) / 100]
            M.great_rebellion(k)
        k.vassals = len(vl)
        k.integration = sum(vl) / len(vl) if vl else 50
        if k.cities == 0:
            return first_decline, None, 'dead', grew
        if not vl:
            return first_decline, y, 'gone', grew
        if nxt == 'renewal':
            k.phase, k.age, k.decay, k.m, k.decline_years = 'renewal', 0, 0.0, mdl.start, 0
        elif nxt in ('prime', 'decline'):
            if nxt == 'decline':
                k.decline_years = 0
                if first_decline is None: first_decline = y
            k.phase = nxt
        elif nxt == 'end':
            return first_decline, None, 'end', grew
    return first_decline, None, 'kept', grew

def q(xs, p):
    xs = sorted(xs)
    return xs[int(p * len(xs))] if xs else '—'

def main(pen):
    mdl = M.Model(small_lord=pen); w = M.World()
    for initial in (False, True):
        print(' 起点：', '读档时已是小宗主，算鼎盛' if initial else '大叛乱打完，从新生开始')
        for own in (2, 3, 4, 5):
            res = [run(s, own, 6, initial, mdl, w) for s in range(2000)]
            fd = [r[0] for r in res if r[0] is not None]
            gone = [r[1] for r in res if r[2] == 'gone']
            share = lambda o: sum(1 for r in res if r[2] == o) / len(res)
            print(f'  自己 {own} 座城：首次衰落 中位 {q(fd,.5)} 年（10%–90%：{q(fd,.1)}–{q(fd,.9)}）'
                  f'  附庸走光 {share("gone"):.0%}，中位 {q(gone,.5)} 年'
                  f'  200 年后仍有附庸 {share("kept"):.0%}  亡国 {share("dead"):.0%}  期间长到 6 城 {sum(r[3] for r in res)/len(res):.0%}')

args = list(map(float, sys.argv[1:4])) + [5.0, 10.0, 1.0][len(sys.argv[1:4]):]
pen, OP_PEN, INT_PEN = args
print(f'== 天命 -{pen:g}/城，附庸好感 -{OP_PEN:g}/城，整合度 -{INT_PEN:g}/城/年')
main(pen)

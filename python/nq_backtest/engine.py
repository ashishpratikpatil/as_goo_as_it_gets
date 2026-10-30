"""Numba ports of the AGIG RevAmt zigzag, the AdvancedSmoothedChannel (SuperSmoother),
and the two NinjaTrader strategies with OnBarClose semantics.

Fill model (NinjaTrader historical, standard fill resolution):
- Orders submitted on bar i's close fill at bar i+1's open.
- Stop and target are live from the entry bar onward and are checked against each
  bar's high/low. When a bar touches both, the stop is assumed to fill first.
- A bar that opens through a stop or target fills at the open.
"""
import numpy as np
from numba import njit

ST_UNDEF, ST_UP, ST_DN = 1, 2, 3

# Trade record columns
T_DAY, T_GROSS, T_COST, T_DIR, T_ENTRY_I, T_EXIT_I, T_KIND = range(7)
KIND_MARKET, KIND_STOP, KIND_TARGET, KIND_EOD = 0, 1, 2, 3


@njit(cache=True)
def nt_atr(h, l, c, period):
    n = len(c)
    out = np.empty(n)
    for i in range(n):
        if i == 0:
            out[i] = h[i] - l[i]
        else:
            tr = max(abs(l[i] - c[i - 1]), max(h[i] - l[i], abs(h[i] - c[i - 1])))
            m = min(i + 1, period)
            out[i] = ((m - 1) * out[i - 1] + tr) / m
    return out


@njit(cache=True)
def agig_zigzag(h, l, c, ema_len, atr_len, atr_rev, pct_rev, abs_rev):
    n = len(c)
    st = np.empty(n, np.int8)
    hb = np.empty(n, np.int64)
    lb = np.empty(n, np.int64)
    atr = nt_atr(h, l, c, max(1, atr_len))
    k = 2.0 / (max(1, ema_len) + 1.0)
    eh = h[0]
    el = l[0]
    state = ST_UNDEF
    mx = eh
    mn = el
    hbar = -1
    lbar = -1
    st[0] = state
    hb[0] = -1
    lb[0] = -1
    for i in range(1, n):
        eh = h[i] * k + eh * (1.0 - k)
        el = l[i] * k + el * (1.0 - k)
        cl = c[i] if c[i] > 0 else eh
        rd = cl * (pct_rev / 100.0) + atr[i] * atr_rev + abs_rev
        new_max = False
        new_min = False
        if state == ST_UNDEF:
            if eh >= mx:
                state = ST_UP
                mx = eh
                new_max = True
            elif el <= mn:
                state = ST_DN
                mn = el
                new_min = True
        elif state == ST_UP:
            if el <= mx - rd:
                state = ST_DN
                mn = el
                new_min = True
            elif eh >= mx:
                mx = eh
                new_max = True
        else:
            if eh >= mn + rd:
                state = ST_UP
                mx = eh
                new_max = True
            elif el <= mn:
                mn = el
                new_min = True
        if new_max:
            hbar = i
        if new_min:
            lbar = i
        st[i] = state
        hb[i] = hbar
        lb[i] = lbar
    return st, hb, lb


@njit(cache=True)
def ss_channel(h, l, c, length):
    """Returns (mean, band unit). Band at multiplier m = mean +/- unit * m (unit already includes pi)."""
    pi = np.pi
    a1 = np.exp(-np.sqrt(2.0) * pi / length)
    b1 = 2.0 * a1 * np.cos(np.sqrt(2.0) * pi / length)
    c3 = -a1 * a1
    c2 = b1
    c1 = 1.0 - c2 - c3
    n = len(c)
    m = np.empty(n)
    r = np.empty(n)
    for i in range(n):
        if i < 2:
            m[i] = c[i]
            r[i] = h[i] - l[i]
        else:
            tr = max(h[i] - l[i], max(abs(h[i] - c[i - 1]), abs(l[i] - c[i - 1])))
            m[i] = c1 * c[i] + c2 * m[i - 1] + c3 * m[i - 2]
            r[i] = c1 * tr + c2 * r[i - 1] + c3 * r[i - 2]
    return m, r * pi


@njit(cache=True)
def round_tick(x, tick):
    return np.floor(x / tick + 0.5) * tick


@njit(cache=True)
def _record(tr, nt, day, gross, cost, d, ent_i, ex_i, kind):
    tr[nt, T_DAY] = day
    tr[nt, T_GROSS] = gross
    tr[nt, T_COST] = cost
    tr[nt, T_DIR] = d
    tr[nt, T_ENTRY_I] = ent_i
    tr[nt, T_EXIT_I] = ex_i
    tr[nt, T_KIND] = kind
    return nt + 1


@njit(cache=True)
def _intrabar(pos, o, h, l, stop, tgt):
    """Returns (filled, price, kind) for the live stop/target on this bar."""
    if pos == 1:
        sh = stop == stop and l <= stop
        th = tgt == tgt and h >= tgt
        if not (sh or th):
            return False, 0.0, 0
        if sh and o <= stop:
            return True, o, KIND_STOP
        if th and o >= tgt:
            return True, o, KIND_TARGET
        if sh:
            return True, stop, KIND_STOP
        return True, tgt, KIND_TARGET
    else:
        sh = stop == stop and h >= stop
        th = tgt == tgt and l <= tgt
        if not (sh or th):
            return False, 0.0, 0
        if sh and o >= stop:
            return True, o, KIND_STOP
        if th and o <= tgt:
            return True, o, KIND_TARGET
        if sh:
            return True, stop, KIND_STOP
        return True, tgt, KIND_TARGET


@njit(cache=True)
def _mr_target(is_short, i, mean, unit, tmode, inner, outer):
    m = mean[i]
    u = unit[i]
    if tmode == 0:      # SD1 on the entry side (R1 for a short, S1 for a long)
        return m + u * inner if is_short else m - u * inner
    elif tmode == 1:    # Mean
        return m
    elif tmode == 2:    # SD1 on the far side (S1 for a short, R1 for a long)
        return m - u * inner if is_short else m + u * inner
    else:               # Opposite outer band (S2 for a short, R2 for a long)
        return m - u * outer if is_short else m + u * outer


@njit(cache=True)
def sim_mr(o, h, l, c, st, hb, lb, mean, unit, open_min, day,
           outer, inner, tmode, buf_pts, reverse, rth_only, brt,
           tick, slip_pts, comm, pv, allow_long=True, allow_short=True):
    """AgigTosRevAmtSwingStrategy with the channel filter and swing stop."""
    n = len(c)
    tr = np.empty((n // 2 + 16, 7))
    nt = 0
    pos = 0
    epx = 0.0
    stop = np.nan
    tgt = np.nan
    ent_i = -1
    pend = 0            # 1 enter long, -1 enter short, 2 exit
    pstop = np.nan
    ptgt = np.nan
    tr_s = -1
    tr_l = -1
    slip_usd = slip_pts * pv
    for i in range(n):
        if pend != 0:
            px = o[i]
            if pos != 0 and (pend == 2 or pend == -pos):
                nt = _record(tr, nt, day[i], (px - epx) * pos * pv, comm + 2 * slip_usd,
                             pos, ent_i, i, KIND_MARKET)
                pos = 0
            if (pend == 1 or pend == -1) and pos == 0:
                pos = pend
                epx = px
                stop = pstop
                tgt = ptgt
                ent_i = i
            pend = 0

        if pos != 0:
            f, px, kind = _intrabar(pos, o[i], h[i], l[i], stop, tgt)
            if f:
                cost = comm + (slip_usd if kind == KIND_TARGET else 2 * slip_usd)
                nt = _record(tr, nt, day[i], (px - epx) * pos * pv, cost, pos, ent_i, i, kind)
                pos = 0

        if i < 2:
            continue

        # RefreshChannelTarget (runs first in OnBarUpdate)
        if pos == -1:
            t = round_tick(_mr_target(True, i, mean, unit, tmode, inner, outer), tick)
            if t > 0:
                if c[i] <= t:
                    pend = 2
                else:
                    tgt = t
        elif pos == 1:
            t = round_tick(_mr_target(False, i, mean, unit, tmode, inner, outer), tick)
            if t > 0:
                if c[i] >= t:
                    pend = 2
                else:
                    tgt = t

        s = st[i]
        hbar = hb[i]
        lbar = lb[i]
        fd = st[i - 1] == ST_UP and s == ST_DN
        fu = st[i - 1] == ST_DN and s == ST_UP
        s_vis = hbar >= 0 and hbar < i and (s == ST_UP or fd)
        l_vis = lbar >= 0 and lbar < i and (s == ST_DN or fu)
        s_entry = s_vis and hbar != tr_s and (hbar == i - 1 or fd)
        l_entry = l_vis and lbar != tr_l and (lbar == i - 1 or fu)
        in_window = (not rth_only) or (open_min[i] >= 570 and open_min[i] < 960)

        if i >= brt and s_entry:
            tr_s = hbar
            if allow_short and h[hbar] >= mean[hbar] + unit[hbar] * outer and in_window and pend != 1 and pend != -1:
                if pos == -1:
                    pass
                elif pos == 1 and not reverse:
                    pend = 2
                else:
                    t = round_tick(_mr_target(True, i, mean, unit, tmode, inner, outer), tick)
                    if t > 0 and t < c[i]:
                        pstop = h[hbar] + buf_pts
                        ptgt = t
                        pend = -1
        if i >= brt and l_entry:
            tr_l = lbar
            if allow_long and l[lbar] <= mean[lbar] - unit[lbar] * outer and in_window and pend != 1 and pend != -1:
                if pos == 1:
                    pass
                elif pos == -1 and not reverse:
                    pend = 2
                else:
                    t = round_tick(_mr_target(False, i, mean, unit, tmode, inner, outer), tick)
                    if t > 0 and t > c[i]:
                        pstop = l[lbar] - buf_pts
                        ptgt = t
                        pend = 1

    if pos != 0:
        nt = _record(tr, nt, day[n - 1], (c[n - 1] - epx) * pos * pv, comm + 2 * slip_usd,
                     pos, ent_i, n - 1, KIND_EOD)
    return tr[:nt]


@njit(cache=True)
def _band_ok(is_long, i, h, l, mean, unit, band_mode, mult, lookback):
    if band_mode == 0:
        return True
    need_above = (band_mode == 1) == is_long
    lb = min(max(0, lookback), i)
    for j in range(lb + 1):
        k = i - j
        if need_above:
            if h[k] >= mean[k] + unit[k] * mult:
                return True
        else:
            if l[k] <= mean[k] - unit[k] * mult:
                return True
    return False


@njit(cache=True)
def sim_orb(o, h, l, c, st, hb, lb, mean, unit, open_min, open_date, day,
            sess_start, sess_end, agg_min, or_min, safe_window, max_trades, exit_at_end,
            band_mode, band_mult, band_lookback, req_agig, exit_mode, stop_mode, buf_pts,
            target_r, brt, tick, slip_pts, comm, pv):
    """AgigOrbSd2Strategy. band_mode 0 = off, 1 = Breakout, 2 = Fade.
    exit_mode 0 = AgigFlip, 1 = OrbOpposite, 2 = ScanArrowsStop. stop_mode 0 = SwingPoint, 1 = OrOpposite."""
    n = len(c)
    tr = np.empty((n // 2 + 16, 7))
    nt = 0
    pos = 0
    epx = 0.0
    stop = np.nan
    tgt = np.nan
    ent_i = -1
    pend = 0
    pstop = np.nan
    ptgt = np.nan
    slip_usd = slip_pts * pv

    bars_in_or = max(1, int(np.floor(or_min / agg_min + 0.5)))
    crosses_midnight = sess_end <= sess_start
    prev_in = False
    prev_key = -1
    bis = 0
    orh_b = 0.0
    orl_b = 0.0
    locked_flag = False
    orh = np.nan
    orl = np.nan
    trades = 0

    for i in range(n):
        if pend != 0:
            px = o[i]
            if pos != 0 and (pend == 2 or pend == -pos):
                nt = _record(tr, nt, day[i], (px - epx) * pos * pv, comm + 2 * slip_usd,
                             pos, ent_i, i, KIND_MARKET)
                pos = 0
            if (pend == 1 or pend == -1) and pos == 0:
                pos = pend
                epx = px
                stop = pstop
                tgt = ptgt
                ent_i = i
            pend = 0

        if pos != 0:
            f, px, kind = _intrabar(pos, o[i], h[i], l[i], stop, tgt)
            if f:
                cost = comm + (slip_usd if kind == KIND_TARGET else 2 * slip_usd)
                nt = _record(tr, nt, day[i], (px - epx) * pos * pv, cost, pos, ent_i, i, kind)
                pos = 0

        t = open_min[i]
        if not crosses_midnight:
            in_s = t >= sess_start and t < sess_end
        else:
            in_s = t >= sess_start or t < sess_end
        key = open_date[i] - 1 if (crosses_midnight and t < sess_start) else open_date[i]
        new_s = in_s and ((not prev_in) or key != prev_key)
        if new_s:
            bis = 1
            orh_b = h[i]
            orl_b = l[i]
            locked_flag = False
            orh = np.nan
            orl = np.nan
            trades = 0
        elif in_s:
            bis += 1
        else:
            bis = 0
            locked_flag = False
            orh = np.nan
            orl = np.nan
        building = in_s and bis <= bars_in_or
        if building and not new_s:
            orh_b = max(orh_b, h[i])
            orl_b = min(orl_b, l[i])
        if in_s and bis > bars_in_or and not locked_flag:
            locked_flag = True
            orh = orh_b
            orl = orl_b
        prev_in = in_s
        prev_key = key

        mte = ((sess_end - t) % 1440 + 1440) % 1440
        locked = in_s and locked_flag
        safe = (not safe_window) or (locked and mte > or_min * 3.5)
        cu = locked and i > 0 and c[i - 1] <= orh and c[i] > orh
        cd = locked and i > 0 and c[i - 1] >= orl and c[i] < orl
        o_long = cu and safe
        o_short = cd and safe

        exited = False
        if pos != 0:
            is_long = pos == 1
            ex = False
            if exit_at_end and ((not in_s) or mte <= agg_min):
                ex = True
            elif exit_mode == 0 and st[i] == (ST_DN if is_long else ST_UP):
                ex = True
            elif exit_mode == 1 and (cd if is_long else cu):
                ex = True
            elif exit_mode == 2 and not (locked and (h[i] > orh if is_long else l[i] < orl)):
                ex = True
            if ex:
                pend = 2
                exited = True

        if (not exited) and i >= brt and pos == 0 and trades < max_trades:
            go = 0
            if o_long and _band_ok(True, i, h, l, mean, unit, band_mode, band_mult, band_lookback) \
                    and ((not req_agig) or st[i] == ST_UP):
                go = 1
            elif o_short and _band_ok(False, i, h, l, mean, unit, band_mode, band_mult, band_lookback) \
                    and ((not req_agig) or st[i] == ST_DN):
                go = -1
            if go != 0:
                s = np.nan
                if stop_mode == 0:
                    sb = lb[i] if go == 1 else hb[i]
                    if sb >= 0 and sb <= i:
                        s = l[sb] - buf_pts if go == 1 else h[sb] + buf_pts
                    if s == s and ((go == 1 and s >= c[i]) or (go == -1 and s <= c[i])):
                        s = np.nan
                if s != s:
                    s = orl - buf_pts if go == 1 else orh + buf_pts
                pstop = round_tick(s, tick)
                ptgt = np.nan
                if target_r > 0:
                    rngp = orh - orl
                    if rngp > 0:
                        step = rngp * target_r
                        tg = orh + step if go == 1 else orl - step
                        while (go == 1 and tg <= c[i]) or (go == -1 and tg >= c[i]):
                            tg += step if go == 1 else -step
                        ptgt = round_tick(tg, tick)
                pend = go
                trades += 1

    if pos != 0:
        nt = _record(tr, nt, day[n - 1], (c[n - 1] - epx) * pos * pv, comm + 2 * slip_usd,
                     pos, ent_i, n - 1, KIND_EOD)
    return tr[:nt]


N_METRICS = 10
METRICS = ("trades", "gross", "net", "sharpe", "sharpe_gross", "maxdd", "pf", "win", "msharpe", "msharpe_gross")


@njit(cache=True)
def stats(tr, n_days, day_month, seg_lo, seg_hi):
    """Per segment: trades, gross $, net $, annualized daily Sharpe (net, gross), max drawdown (net, trade
    sequence), profit factor (net), win rate (net), monthly Sharpe (net, gross; mean/std, not annualized)."""
    dg = np.zeros(n_days)
    dn = np.zeros(n_days)
    nt = tr.shape[0]
    for k in range(nt):
        d = int(tr[k, T_DAY])
        dg[d] += tr[k, T_GROSS]
        dn[d] += tr[k, T_GROSS] - tr[k, T_COST]
    nseg = len(seg_lo)
    out = np.zeros((nseg, N_METRICS))
    for s in range(nseg):
        lo = seg_lo[s]
        hi = seg_hi[s]
        nd = hi - lo
        if nd < 2:
            continue
        mn = 0.0
        mg = 0.0
        for d in range(lo, hi):
            mn += dn[d]
            mg += dg[d]
        mn /= nd
        mg /= nd
        vn = 0.0
        vg = 0.0
        for d in range(lo, hi):
            vn += (dn[d] - mn) ** 2
            vg += (dg[d] - mg) ** 2
        sdn = np.sqrt(vn / (nd - 1))
        sdg = np.sqrt(vg / (nd - 1))
        out[s, 3] = mn / sdn * np.sqrt(252.0) if sdn > 0 else 0.0
        out[s, 4] = mg / sdg * np.sqrt(252.0) if sdg > 0 else 0.0

        cnt = 0
        wins = 0
        gp = 0.0
        gl = 0.0
        gross = 0.0
        net = 0.0
        cum = 0.0
        peak = 0.0
        mdd = 0.0
        for k in range(nt):
            d = int(tr[k, T_DAY])
            if d < lo or d >= hi:
                continue
            p = tr[k, T_GROSS] - tr[k, T_COST]
            cnt += 1
            gross += tr[k, T_GROSS]
            net += p
            if p > 0:
                wins += 1
                gp += p
            else:
                gl -= p
            cum += p
            if cum > peak:
                peak = cum
            if peak - cum > mdd:
                mdd = peak - cum
        out[s, 0] = cnt
        out[s, 1] = gross
        out[s, 2] = net
        out[s, 5] = -mdd
        out[s, 6] = gp / gl if gl > 0 else (99.0 if gp > 0 else 0.0)
        out[s, 7] = wins / cnt if cnt > 0 else 0.0

        m0 = day_month[lo]
        m1 = day_month[hi - 1]
        nm = m1 - m0 + 1
        if nm >= 2:
            mon_n = np.zeros(nm)
            mon_g = np.zeros(nm)
            for d in range(lo, hi):
                mon_n[day_month[d] - m0] += dn[d]
                mon_g[day_month[d] - m0] += dg[d]
            a = mon_n.mean()
            b = mon_g.mean()
            sa = np.sqrt(((mon_n - a) ** 2).sum() / (nm - 1))
            sb = np.sqrt(((mon_g - b) ** 2).sum() / (nm - 1))
            out[s, 8] = a / sa if sa > 0 else 0.0
            out[s, 9] = b / sb if sb > 0 else 0.0
    return out

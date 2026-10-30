"""Regime metrics on daily NQ vs monthly P&L of the recommended ORB and mean-revert settings.

Daily series: Yahoo NQ=F, 2020-01-01 through 2026-10-21.
Intraday series: Dataset_NQ_1min_2022_2025.csv, 5-minute bars.
"""
import json
import numpy as np
import pandas as pd
import yfinance as yf

import data
import detail as D
import engine as E

OUT = "results/regime.json"

ORB = dict(tf=5, atr_len=5, atr_rev=2.0, or_min=5, band_mode=2, band_mult=2.80,
           band_lb_min=15, chan=300, req_agig=0, exit_mode=2, stop_mode=1, buf=0,
           target_r=2.0, max_trades=1)
MR = dict(tf=15, atr_len=14, atr_rev=1.5, chan=200, outer=2.0, tmode=3, buf=4,
          reverse=0, rth=0, allow_short=False)


def er(close, n=20):
    d = close.diff(n).abs()
    v = close.diff().abs().rolling(n).sum()
    return d / v.replace(0, np.nan)


def hurst_vr(rets, q=10):
    r = rets.dropna()
    if len(r) < q * 4:
        return np.nan
    v1 = r.var(ddof=1)
    vq = r.rolling(q).sum().dropna().var(ddof=1)
    if v1 <= 0:
        return np.nan
    return float(vq / (q * v1))


def adx_like(h, l, c, n=14):
    up = h.diff()
    dn = -l.diff()
    plus_dm = np.where((up > dn) & (up > 0), up, 0.0)
    minus_dm = np.where((dn > up) & (dn > 0), dn, 0.0)
    tr = pd.concat([(h - l), (h - c.shift()).abs(), (l - c.shift()).abs()], axis=1).max(axis=1)
    atr = pd.Series(tr, index=h.index).ewm(alpha=1 / n, adjust=False).mean()
    pdi = 100 * pd.Series(plus_dm, index=h.index).ewm(alpha=1 / n, adjust=False).mean() / atr
    mdi = 100 * pd.Series(minus_dm, index=h.index).ewm(alpha=1 / n, adjust=False).mean() / atr
    dx = 100 * (pdi - mdi).abs() / (pdi + mdi).replace(0, np.nan)
    return dx.ewm(alpha=1 / n, adjust=False).mean()


def yearly_block(df):
    rets = np.log(df["Close"]).diff()
    out = []
    for y, g in df.groupby(df.index.year):
        r = np.log(g["Close"]).diff().dropna()
        rng = (g["High"] - g["Low"]).replace(0, np.nan)
        body = (g["Close"] - g["Open"]).abs()
        trend_day = (body / rng) >= 0.60
        close_pos = (g["Close"] - g["Low"]) / rng
        out.append({
            "year": int(y),
            "ret_pct": round(float((g["Close"].iloc[-1] / g["Close"].iloc[0] - 1) * 100), 1),
            "ann_vol": round(float(r.std() * np.sqrt(252) * 100), 1),
            "er20": round(float(er(g["Close"], 20).median()), 3),
            "hurst10": round(hurst_vr(r, 10), 3),
            "lag1": round(float(r.autocorr(1)), 3),
            "adx": round(float(adx_like(g["High"], g["Low"], g["Close"]).median()), 1),
            "trend_day_pct": round(float(trend_day.mean() * 100), 1),
            "range_pct": round(float((rng / g["Close"]).median() * 100), 2),
            "close_in_ext": round(float(((close_pos <= 0.2) | (close_pos >= 0.8)).mean() * 100), 1),
            "up_days": round(float((r > 0).mean() * 100), 1),
        })
    return out


def monthly_daily(df):
    rets = np.log(df["Close"]).diff()
    rng = (df["High"] - df["Low"]).replace(0, np.nan)
    body = (df["Close"] - df["Open"]).abs()
    m = pd.DataFrame({
        "ret": rets,
        "er20": er(df["Close"], 20),
        "adx": adx_like(df["High"], df["Low"], df["Close"]),
        "trend_day": (body / rng) >= 0.60,
        "range_pct": rng / df["Close"],
        "vol20": rets.rolling(20).std() * np.sqrt(252),
    })
    g = m.resample("ME").agg(
        ret=("ret", "sum"),
        er20=("er20", "median"),
        adx=("adx", "median"),
        trend_day=("trend_day", "mean"),
        range_pct=("range_pct", "median"),
        vol20=("vol20", "median"),
        hurst=("ret", lambda s: hurst_vr(s, 5)),
        lag1=("ret", lambda s: s.autocorr(1) if len(s.dropna()) > 8 else np.nan),
    )
    g["ret_pct"] = (np.exp(g["ret"]) - 1) * 100
    g["trend_day_pct"] = g["trend_day"] * 100
    return g


def trade_months(a, tr):
    ts = pd.to_datetime(a["close_ns"][tr[:, E.T_EXIT_I].astype(int)])
    net = tr[:, E.T_GROSS] - tr[:, E.T_COST]
    s = pd.Series(net, index=ts).resample("ME").sum()
    n = pd.Series(1, index=ts).resample("ME").sum()
    return pd.DataFrame({"pnl": s, "trades": n})


def rth_day_stats(a, tf=5):
    ts = pd.to_datetime(a["close_ns"])
    rth = (a["open_min"] >= 570) & (a["open_min"] < 960)
    df = pd.DataFrame({
        "o": a["o"], "h": a["h"], "l": a["l"], "c": a["c"],
        "day": a["day"],
    }, index=ts)[rth]
    g = df.groupby("day").agg(o=("o", "first"), h=("h", "max"), l=("l", "min"), c=("c", "last"))
    rng = (g["h"] - g["l"]).replace(0, np.nan)
    body = (g["c"] - g["o"]).abs()
    days = pd.to_datetime(a["days_ns"])
    out = pd.DataFrame({
        "trend_day": (body / rng) >= 0.60,
        "range_pts": (g["h"] - g["l"]).to_numpy(),
        "or_proxy": np.nan,  # filled below from first 5 min high-low
    }, index=days[g.index.to_numpy()])
    first = df.groupby("day").head(1)
    or_rng = (first["h"] - first["l"]).to_numpy()
    out["or_proxy"] = or_rng
    out["or_frac"] = out["or_proxy"] / out["range_pts"]
    out["ext_r"] = (out["range_pts"] - out["or_proxy"]) / out["or_proxy"].replace(0, np.nan)
    return out


def corr_lag(pnl, feat):
    x = pd.concat([feat.shift(1).rename("f"), pnl.rename("p")], axis=1).dropna()
    if len(x) < 8:
        return None
    r = float(x["f"].corr(x["p"]))
    # simple gate: trade only when last month's feature is above its median
    med = x["f"].median()
    on = x["p"][x["f"] >= med].sum()
    off = x["p"][x["f"] < med].sum()
    return {"corr": round(r, 3), "median": round(float(med), 4),
            "pnl_when_high": round(float(on), 0), "pnl_when_low": round(float(off), 0),
            "n_high": int((x["f"] >= med).sum()), "n_low": int((x["f"] < med).sum())}


def main():
    print("downloading NQ=F ...")
    raw = yf.download("NQ=F", start="2020-01-01", end="2026-10-22", auto_adjust=True, progress=False)
    if isinstance(raw.columns, pd.MultiIndex):
        raw.columns = raw.columns.get_level_values(0)
    df = raw.dropna(subset=["Close"]).copy()
    df.index = pd.to_datetime(df.index).tz_localize(None)
    years = yearly_block(df)
    print(pd.DataFrame(years).to_string(index=False))

    print("\nrunning strategies ...")
    a5, tr_orb = D.run_orb(ORB)
    a15, tr_mr = D.run_mr(MR)
    orb_m = trade_months(a5, tr_orb)
    mr_m = trade_months(a15, tr_mr)
    rth = rth_day_stats(a5)
    rth_m = rth.resample("ME").agg(
        trend_day=("trend_day", "mean"),
        range_pts=("range_pts", "median"),
        or_pts=("or_proxy", "median"),
        ext_r=("ext_r", "median"),
        or_frac=("or_frac", "median"),
    )
    daily_m = monthly_daily(df)

    aligned = pd.concat({
        "orb": orb_m["pnl"], "mr": mr_m["pnl"],
        "er20": daily_m["er20"], "adx": daily_m["adx"],
        "trend_day": daily_m["trend_day_pct"], "vol20": daily_m["vol20"],
        "lag1": daily_m["lag1"], "nq_ret": daily_m["ret_pct"],
        "rth_trend": rth_m["trend_day"] * 100,
        "ext_r": rth_m["ext_r"], "or_frac": rth_m["or_frac"],
        "range_pts": rth_m["range_pts"],
    }, axis=1).dropna(how="all")

    # 2023-2025 months only for strategy correlation
    win = aligned.loc["2023-01-01":"2025-12-31"].copy()
    feats = ["er20", "adx", "trend_day", "vol20", "lag1", "nq_ret", "rth_trend", "ext_r", "or_frac"]
    gates = {}
    for strat in ("orb", "mr"):
        gates[strat] = {f: corr_lag(win[strat], win[f]) for f in feats}

    print("\nlag-1 month correlation with next-month ORB P&L")
    for k, v in gates["orb"].items():
        print(k, v)

    # Walk-forward style: classify each month from prior 20 sessions on daily data
    d = df.copy()
    d["er20"] = er(d["Close"], 20)
    d["adx"] = adx_like(d["High"], d["Low"], d["Close"])
    rng = (d["High"] - d["Low"]).replace(0, np.nan)
    d["trend_day"] = ((d["Close"] - d["Open"]).abs() / rng) >= 0.60
    # rolling 20-day trend-day rate and ER at each month end
    month_end = d.resample("ME").last()
    roll_td = d["trend_day"].rolling(20).mean()
    month_end["td20"] = roll_td.reindex(month_end.index, method="ffill")
    month_end["er20"] = d["er20"].reindex(month_end.index, method="ffill")
    month_end["adx"] = d["adx"].reindex(month_end.index, method="ffill")

    # Year summary of those end-of-year snapshots / yearly medians already in years

    # Monthly series for charts 2020-2026
    chart = daily_m.loc["2020-01-01":].copy()
    chart["year"] = chart.index.year
    chart["month"] = chart.index.strftime("%Y-%m")

    orb_chart = orb_m.loc["2023-01-01":"2025-12-31"]
    months = [t.strftime("%Y-%m") for t in orb_chart.index]

    payload = {
        "years": years,
        "gates_orb": gates["orb"],
        "gates_mr": gates["mr"],
        "monthly": {
            "labels": [t.strftime("%Y-%m") for t in chart.index],
            "er20": [None if pd.isna(x) else round(float(x), 3) for x in chart["er20"]],
            "adx": [None if pd.isna(x) else round(float(x), 1) for x in chart["adx"]],
            "trend_day": [None if pd.isna(x) else round(float(x), 1) for x in chart["trend_day_pct"]],
            "vol": [None if pd.isna(x) else round(float(x) * 100, 1) for x in chart["vol20"]],
            "nq_ret": [None if pd.isna(x) else round(float(x), 1) for x in chart["ret_pct"]],
        },
        "orb_months": {
            "labels": months,
            "pnl": [round(float(x), 0) for x in orb_chart["pnl"]],
            "trades": [int(x) for x in orb_chart["trades"]],
        },
        "mr_months": {
            "labels": [t.strftime("%Y-%m") for t in mr_m.loc["2023-01-01":"2025-12-31"].index],
            "pnl": [round(float(x), 0) for x in mr_m.loc["2023-01-01":"2025-12-31"]["pnl"]],
        },
        "rth_year": rth.groupby(rth.index.year).agg(
            trend_day=("trend_day", "mean"),
            ext_r=("ext_r", "median"),
            or_pts=("or_proxy", "median"),
            range_pts=("range_pts", "median"),
        ).round(3).reset_index().rename(columns={"index": "year"}).to_dict("records"),
    }
    # fix rth_year years
    payload["rth_year"] = []
    gy = rth.groupby(rth.index.year)
    for y, g in gy:
        payload["rth_year"].append({
            "year": int(y),
            "trend_day_pct": round(float(g["trend_day"].mean() * 100), 1),
            "ext_r": round(float(g["ext_r"].median()), 2),
            "or_pts": round(float(g["or_proxy"].median()), 1),
            "range_pts": round(float(g["range_pts"].median()), 1),
        })

    # Losing vs winning months of ORB: feature snapshot the month before
    w = win.dropna(subset=["orb"]).copy()
    w["prev_er"] = w["er20"].shift(1)
    w["prev_td"] = w["trend_day"].shift(1)
    w["prev_ext"] = w["ext_r"].shift(1)
    w["prev_vol"] = w["vol20"].shift(1)
    lose = w[w["orb"] < 0]
    winm = w[w["orb"] > 0]
    payload["orb_win_lose"] = {
        "n_win": int(len(winm)), "n_lose": int(len(lose)),
        "win_sum": round(float(winm["orb"].sum()), 0),
        "lose_sum": round(float(lose["orb"].sum()), 0),
        "prev_er_win": round(float(winm["prev_er"].median()), 3),
        "prev_er_lose": round(float(lose["prev_er"].median()), 3),
        "prev_td_win": round(float(winm["prev_td"].median()), 1),
        "prev_td_lose": round(float(lose["prev_td"].median()), 1),
        "prev_ext_win": round(float(winm["prev_ext"].median()), 2),
        "prev_ext_lose": round(float(lose["prev_ext"].median()), 2),
        "prev_vol_win": round(float(winm["prev_vol"].median() * 100), 1),
        "prev_vol_lose": round(float(lose["prev_vol"].median() * 100), 1),
    }

    # Proposed gate on daily NQ: ER20 > 0.25 AND ADX > 20, evaluated at prior month end
    # Apply to 2020-2026 yearly: fraction of days that would be "on"
    d["on"] = (d["er20"] > 0.25) & (d["adx"] > 20)
    on_year = []
    for y, g in d.groupby(d.index.year):
        on_year.append({
            "year": int(y),
            "pct_on": round(float(g["on"].mean() * 100), 1),
            "er20": round(float(g["er20"].median()), 3),
            "adx": round(float(g["adx"].median()), 1),
        })
    payload["gate_coverage"] = on_year

    # If we had traded ORB only when prior month ER20 > 0.28 (typical 2022 is lower)
    gorb = corr_lag(win["orb"], win["er20"])
    payload["note_er_gate"] = gorb

    with open(OUT, "w") as f:
        json.dump(payload, f, indent=2)
    print("saved", OUT)


if __name__ == "__main__":
    main()

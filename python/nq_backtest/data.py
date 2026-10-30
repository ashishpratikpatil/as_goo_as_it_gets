"""Load the NQ 1-minute CSV and build close-stamped bars for each timeframe.

The CSV timestamps are bar close times in US/Eastern (first Globex bar is 18:01,
last is 17:00), the same convention NinjaTrader uses for minute bars.
"""
import os
import numpy as np
import pandas as pd

CSV = os.environ.get(
    "AGIG_NQ_CSV",
    r"C:\Users\ashis\Downloads\archive\Dataset_NQ_1min_2022_2025.csv",
)
CACHE = os.path.join(os.path.dirname(os.path.abspath(__file__)), "cache")
TIMEFRAMES = (1, 2, 3, 5, 10, 15)

IS_END = pd.Timestamp("2025-01-01")


def load_1min():
    df = pd.read_csv(CSV, usecols=[0, 1, 2, 3, 4, 5])
    df.columns = ["ts", "open", "high", "low", "close", "volume"]
    df["ts"] = pd.to_datetime(df["ts"], format="%m/%d/%Y %H:%M")
    return df.set_index("ts").sort_index()


def resample(df1, tf):
    if tf == 1:
        return df1.copy()
    r = df1.resample(f"{tf}min", closed="right", label="right")
    out = pd.DataFrame({
        "open": r["open"].first(),
        "high": r["high"].max(),
        "low": r["low"].min(),
        "close": r["close"].last(),
        "volume": r["volume"].sum(),
    })
    return out.dropna(subset=["open"])


def to_arrays(df, tf):
    close_ts = df.index
    open_ts = close_ts - pd.Timedelta(minutes=tf)
    open_min = (open_ts.hour * 60 + open_ts.minute).to_numpy(np.int64)
    open_date = (open_ts.normalize().asi8 // (86400 * 10**9)).astype(np.int64)
    # Trading day: the Globex session that opens at 18:00 belongs to the next date.
    sess_date = (close_ts + pd.Timedelta(hours=6)).normalize()
    days, day_idx = np.unique(sess_date.asi8, return_inverse=True)
    days = pd.to_datetime(days)
    month_key = days.year * 12 + days.month
    _, day_month = np.unique(month_key, return_inverse=True)
    return dict(
        o=df["open"].to_numpy(np.float64),
        h=df["high"].to_numpy(np.float64),
        l=df["low"].to_numpy(np.float64),
        c=df["close"].to_numpy(np.float64),
        close_ns=close_ts.asi8,
        open_min=open_min,
        open_date=open_date,
        day=day_idx.astype(np.int64),
        days_ns=days.asi8,
        day_month=day_month.astype(np.int64),
    )


def build_cache():
    os.makedirs(CACHE, exist_ok=True)
    df1 = load_1min()
    for tf in TIMEFRAMES:
        arr = to_arrays(resample(df1, tf), tf)
        np.savez(os.path.join(CACHE, f"nq_{tf}m.npz"), **arr)
        print(tf, "min bars:", len(arr["c"]), "days:", len(arr["days_ns"]))


def load_tf(tf):
    path = os.path.join(CACHE, f"nq_{tf}m.npz")
    if not os.path.exists(path):
        build_cache()
    z = np.load(path)
    return {k: z[k] for k in z.files}


def segments(days_ns):
    """Day-index ranges: 2023 (with the last days of 2022), 2024, 2025, 2023-2024, all."""
    d = pd.to_datetime(days_ns)
    n = len(d)
    i24 = int(np.searchsorted(d, pd.Timestamp("2024-01-01")))
    i25 = int(np.searchsorted(d, IS_END))
    lo = np.array([0, i24, i25, 0, 0], np.int64)
    hi = np.array([i24, i25, n, i25, n], np.int64)
    return lo, hi


SEG_NAMES = ("y2023", "y2024", "y2025", "is", "all")


if __name__ == "__main__":
    build_cache()

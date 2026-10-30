import detail as D

P = dict(tf=5, atr_len=5, atr_rev=2.0, or_min=5, band_mode=2, chan=300, req_agig=0,
         exit_mode=2, stop_mode=1, buf=0, max_trades=1)
for label, extra in (
    ("recommended: Fade outer 2.415 lookback 15 2R", dict(band_mult=2.415, band_lb_min=15, target_r=2.0)),
    ("fine-grid: Fade outer 2.80 lookback 15 2R", dict(band_mult=2.80, band_lb_min=15, target_r=2.0)),
    ("fine-grid: Fade outer 2.80 lookback 20 1R", dict(band_mult=2.80, band_lb_min=20, target_r=1.0, stop_mode=0)),
):
    a, tr = D.run_orb({**P, **extra})
    D.report(label, a, tr)

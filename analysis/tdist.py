"""Quantile of Student's t distribution without SciPy (the same functions as in config_d.py)."""
import math


def _betacf(a, b, x):
    """continued fraction of the regularized incomplete beta function (modified Lentz)"""
    tiny, c, d = 1e-300, 1.0, 1.0 - (a + b) * x / (a + 1.0)
    d = 1.0 / (d if abs(d) > tiny else tiny)
    h = d
    for m in range(1, 500):
        for aa in (m * (b - m) * x / ((a + 2 * m - 1) * (a + 2 * m)),
                   -(a + m) * (a + b + m) * x / ((a + 2 * m) * (a + 2 * m + 1))):
            d = 1.0 + aa * d
            d = 1.0 / (d if abs(d) > tiny else tiny)
            c = 1.0 + aa / c
            c = c if abs(c) > tiny else tiny
            h *= d * c
        if abs(d * c - 1.0) < 1e-15:
            break
    return h


def _betainc(a, b, x):
    lb = math.lgamma(a + b) - math.lgamma(a) - math.lgamma(b) + a * math.log(x) + b * math.log(1.0 - x)
    if x < (a + 1.0) / (a + b + 2.0):
        return math.exp(lb) * _betacf(a, b, x) / a
    return 1.0 - math.exp(lb) * _betacf(b, a, 1.0 - x) / b


def t_quantile(p, df):
    """quantile of Student's t distribution for p > 0.5, by bisection of the distribution function"""
    lo, hi = 0.0, 1e4
    for _ in range(200):
        t = (lo + hi) / 2
        if 1.0 - 0.5 * _betainc(df / 2.0, 0.5, df / (df + t * t)) < p:
            lo = t
        else:
            hi = t
    return (lo + hi) / 2

# -*- coding: utf-8 -*-
"""Paths shared by the analysis scripts (all relative to the repository root)."""
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
DATA = ROOT / 'data'
RESULTS = ROOT / 'results'
OUT = ROOT / 'analysis' / 'output'

INPUTS = ['uniform', 'en', 'ru', 'zip', 'zero', 'ff']


def out_path(name):
    OUT.mkdir(parents=True, exist_ok=True)
    return OUT / name


def config_dir(arg):
    """'B' or 'C' -> results/configuration_B|C; any other argument is taken as a directory."""
    if arg.upper() in ('B', 'C'):
        return RESULTS / f'configuration_{arg.upper()}', arg.upper()
    p = Path(arg).resolve()
    return p, p.name.replace('configuration_', '')

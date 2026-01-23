# MSDF Orbitron

Font source : orbitron by google, at https://fonts.google.com/specimen/Orbitron.

License : SIL Open Font License (https://openfontlicense.org/).

Derivative work : a subset of characters (`charset.txt`) has been converted to [MSDF format](https://github.com/Chlumsky/msdf-atlas-gen) :
```bash
./msdf-atlas-gen.exe -font Orbitron-Regular.ttf -charset charset.txt -imageout orbitron.png -json orbitron.metrics.json -potr -uniformgrid
```

Uses default 2px SDF range.

MSDF-atlas-gen does not detect the newer kerning format in the downloaded Orbitron.
To generate the proper kerning data, open the font with `fontforge` and export it with legacy kern data.
See https://github.com/Chlumsky/msdf-atlas-gen/issues/4 for more details.

`orbitron.metrics.json` is referenced by the `MaterialPropertyDrawer` to properly position letters.
It **must** be placed alongside the `orbitron.png` font MSDF texture, so that the `MaterialPropertyDrawer` can locate it from the texture asset path (replace extension by `.metrics.json`).

Charset
```
[32, 126]
"àâåéèêëïôùÿ"
"“”–—×°"
```
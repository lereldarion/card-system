# MSDF Orbitron

Font source : orbitron by google, at https://fonts.google.com/specimen/Orbitron.

License : SIL Open Font License (https://openfontlicense.org/).

Derivative work : a subset of characters (`charset.txt`) has been converted to [MSDF format](https://github.com/Chlumsky/msdf-atlas-gen) :
```bash
./msdf-atlas-gen.exe -font Orbitron-Regular.ttf -charset charset.txt -imageout orbitron.png -json orbitron.metrics.json -potr -uniformgrid
```

Charset
```
[32, 126]
"àâåéèêëïôùÿ"
"“”–—×°"
```

TODO missing 'ç' but wait until some use to regenerate with an expanded charset.
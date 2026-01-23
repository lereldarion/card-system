# Lereldarion Card System
Shader system for rendering cards (TCG).
A TCG card with 1 material slot, 1 quad, configurable text !

Currently only used for cards for the *Exploreurs Imaginaires* community.

# Install
- using VCC at https://lereldarion.github.io/vpm-listing/.
- or from Unity Packages from [releases](https://github.com/lereldarion/card-system/releases).

[Github repository](https://github.com/lereldarion/card-system/) in case you found this documentation from an installed package.

# How to use

TODO

# Shader documentation
- The card UI design is done using a *signed distance function* (SDF) to generate a sharp UI (https://iquilezles.org/articles/distfunctions2d/).
- Sharp text is done using the [MSDF](https://github.com/Chlumsky/msdf-atlas-gen) strategy. A TTF font can be converted to an MSDF texture grid of glyphs.
- Text lines are encoded in a texture.
- A custom `MaterialPropertyDrawer` enables ergonomic edition of the text line texture.

# TODO
- Kerning support
- MaterialPropertyDrawer load from texture.
- Fix invert impl
- Back of the card (proper)
- instanced version ? requires tex2D array + fixed instanced id. Ergonomy of the UI will be crap.
- Package release when operational
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
- MaterialPropertyDrawer load from texture.
- Back of the card (proper)
- Change line order with buttons ? May be annoying if text focus does not like it
- Package release when operational

Make an instanced version of the shader for deck/collection of cards ?
- Requires tex2D array + fixed instanced id. Textures cannot be instanced.
- Resolution of all instanced textures must be fixed. Foreground, background, text encodings.
- UI ergonomy is a hard problem.
    - Conversion requires all renderers at once to build the tex2Darray, and set fixed instance id. Keep the old materials around afterward, even if unused ?
    - Editing a specific card, un-merging, or adding a card is difficult.
    - Likely waste of memory if number of slices (instances, cards) of the texture must be power of 2
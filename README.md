# Lereldarion Card System
Shader system for rendering cards (TCG).
A TCG card with 1 material slot, 1 quad, configurable text using SDF encoding to stay sharp.

Currently only used for cards for the *Exploreurs Imaginaires* community.

# Install
- using VCC at https://lereldarion.github.io/vpm-listing/.
- or from Unity Packages from [releases](https://github.com/lereldarion/card-system/releases).

[Github repository](https://github.com/lereldarion/card-system/) in case you found this documentation from an installed package.

# How to make "Explorer" cards
1. Install the package using VCC/ALCOM or manual unitypackage.
2. Use the `Explorer/card.fbx` mesh for cards. Its aspect ratio matches the design the shader expect, a normal quad will not work correctly.
3. Create a new `Material` using the `Lereldarion/Card/Explorer` shader. Some properties will be filled by default (font).
4. Set a pair of foreground (avatar) / background textures.
    - Use the camera in *multi-layer* mode to generate a pair of textures, and crop/resize them.
    - Foreground should have alpha (DXT5 / BC7)
    - Background should not use alpha to reduce VRAM (DXT1)
    - 512x512 textures are enough for a card size.
    - Both have a parallax effect. Reduce the tiling values to slightly below 1 (0.9) to avoid hitting the edges when viewed at an angle.
5. Use the text edition GUI to add lines of text. TODO provide an example with all kind of features correctly placed.

# Technical shader documentation
The card UI design is done using a *signed distance function* (SDF) to generate a sharp UI (https://iquilezles.org/articles/distfunctions2d/).
See the [explorer card an example](Explorer/Card.shader).

## Text line system
Sharp text is done using the [MSDF](https://github.com/Chlumsky/msdf-atlas-gen) strategy.
A TTF font can be converted to an MSDF texture grid of glyphs, that is then sampled and combined to form the final card SDF.

Text lines are encoded in an [R32G32B32A32_SFloat](https://docs.unity3d.com/ScriptReference/Experimental.Rendering.GraphicsFormat.R32G32B32A32_SFloat.html) 2D texture, one line per row, with 1 control pixel followed by glyph references (4 per pixel).
A custom [MaterialPropertyDrawer](https://docs.unity3d.com/ScriptReference/MaterialPropertyDrawer.html) provides a convenient GUI to edit the text ; it reads and regenerates the encoding texture.
```
[LereldarionCardTextLines(_Font_MSDF_Atlas_Texture, _Font_MSDF_Atlas_Config, _Text_LineCount)] _Text_Encoding_Texture("Text lines", 2D) = "" {}
[HideInInspector] _Text_LineCount("Text line count", Integer) = 0 // Auto-generated text metadata
[NoScaleOffset] _Font_MSDF_Atlas_Texture("Font texture (MSDF)", 2D) = "" {} // Font choice
[HideInInspector] _Font_MSDF_Atlas_Config("Font config", Vector) = (0, 0, 0, 0) // Auto-generated font metadata
```

Text editor features :
- Edit multiple lines of text. Avoid using hundreds of lines, as each line at least cost a bounding box check per pixel.
- Line position : each line has an UV offset, Size in UV units, Rotation in degrees
- Supports inverted text : characters are "holes" in a filled rectangle bounding box. Use spaces to pad the box sizes. Box height is not adjustable, only overall font size.
- Read from / write to an encoding texture. The texture can be safely renamed, its default generated name is `path/to/material.text_encoding_property_name.asset`.
- Indicates if the font does not support some characters
- Swapping fonts : set the new font texture, and save if all characters are supported. This will regenerate the encoding texture with the new font encoding
- Possible integration into shaders with custom editors, due to using a [MaterialPropertyDrawer](https://docs.unity3d.com/ScriptReference/MaterialPropertyDrawer.html) and not a full [ShaderGUI](https://docs.unity3d.com/ScriptReference/ShaderGUI.html)

See the [explorer card](Explorer/Card.shader) as an example of use, and the [material drawer code](Editor/LereldarionCardTextLinesDrawer.cs) for technical details.

## Font
The text line system requires an [MSDF](https://github.com/Chlumsky/msdf-atlas-gen) font in *uniform grid* mode.
The explorer card uses Orbitron, and was generated using this command :
```bash
./msdf-atlas-gen.exe -font Orbitron-Regular.ttf -charset charset.txt -imageout orbitron.png -json orbitron.metrics.json -potr -uniformgrid
```

You can generate your own font and use it with the system
- You must select a charset ahead of time, see the charset selection options provided by `msdf-atlas-gen`.
- The text system supports both square and rectangle textures (`-potr`)
- `msdf-atlas-gen` defaults for other options work well in my experience : texture size selection, and texture SDF range (default = 2px)
- The `font_texture.metrics.json` file is **required** by the `MaterialPropertyDrawer` to be able to understand the font texture. It must have the same path as the texture with extension replaced by `.metrics.json` : `path/to/orbitron.png` → `path/to/orbitron.metrics.json`.
- It is recommended to reuse the same font texture across multiple materials, to share the VRAM. Text encodings are tiny.
- The text encoding texture does not store the exact text line strings, but encodings of them. Some consequences :
    - Changing the font of a material will prevent correct loading of already encoded text.
    - Having multiple whitespace in a charset may confuse the decoder, as it must deduce spaces from glyph spacing.

The text system supports fonts with [kerning](https://en.wikipedia.org/wiki/Kerning).
However `msdf-atlas-gen` only supports older kerning data format (https://github.com/Chlumsky/msdf-atlas-gen/issues/4).
If your font kerning is not working, the solution is to open the font with [fontforge](https://fontforge.org), export it with legacy kern data, and run `msdf-atlas-gen` on it.
Check if the generated `metrics.json` contains a non empty `kernings` JSON field.

# TODO
Required
- Package release

Maybe
- PBR material, foil effects ?
- Change line order with buttons ? May be annoying if text focus does not like it
- Some space left in line control pixel for storing config : boldness, color ?

Make an instanced version of the shader for deck/collection of cards ?
- Requires tex2D array + fixed instanced id. Textures cannot be instanced.
- Resolution of all instanced textures must be fixed. Foreground, background, text encodings.
- UI ergonomy is a hard problem.
    - Conversion requires all renderers at once to build the tex2Darray, and set fixed instance id. Keep the old materials around afterward, even if unused ?
    - Editing a specific card, un-merging, or adding a card is difficult.
    - Likely waste of memory if number of slices (instances, cards) of the texture must be power of 2
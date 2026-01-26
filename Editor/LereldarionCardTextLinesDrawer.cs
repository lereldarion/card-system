// Made by Lereldarion (https://github.com/lereldarion/)
// Free to redistribute under the MIT license
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using System.Collections.Generic;
using System.Linq;
using System;
using Unity.Collections;

// Editor interface for text encoded to texture tables.
//
// Usage : tag a set of properties that encode text.
// [LereldarionCardTextLines(_Font_MSDF_Atlas_Texture, _Font_MSDF_Atlas_Config, _Text_LineCount)] _Text_Encoding_Texture("Text lines", 2D) = "" {}
// [HideInInspector] _Text_LineCount("Text line count", Integer) = 0 // Auto-generated text metadata
// [NoScaleOffset] _Font_MSDF_Atlas_Texture("Font texture (MSDF)", 2D) = "" {} // Font choice
// [HideInInspector] _Font_MSDF_Atlas_Config("Font config", Vector) = (0, 0, 0, 0) // Auto-generated font metadata
//
// _Text_Encoding_Texture texture asset (path/to/font_texture.<ext>) must have a companion path/to/font_texture.metrics.json with glyph metadata.
//
// Offset is in input UV.
// Size is set so that 1 input UV.y unit goes from font baseline to ascender height (https://en.wikipedia.org/wiki/Typeface#Font_metrics).
// Rotation is in degrees.
// Shader-side, a signed distance function at input UV scale is returned, with negative = interior of characters.
public class LereldarionCardTextLinesDrawer : MaterialPropertyDrawer
{
    // Values from shader property arguments
    private readonly string font_texture_property_name = null;
    private readonly string font_config_property_name = null;
    private readonly string line_count_property_name = null;

    // Caching state. Avoid reloading font metrics and text everytime.
    // Keep track of what was already validated or error.
    private CachedState cache_state = CachedState.None;
    private enum CachedState { None, SelectionAndShaderProperties, Font, Text }
    private Material current_material_selection = null;
    private Texture current_font_texture = null;
    private Font font = null;
    private Texture current_encoding_texture = null;
    private string current_encoding_texture_asset_path = "";
    private LineCache line_cache;

    // GUI state
    private bool gui_section_foldout = true;
    private string gui_error = "";

    public LereldarionCardTextLinesDrawer(string font_texture_property_name, string font_config_property_name, string line_count_property_name)
    {
        // Unity already splits by ',' and trims whitespace
        this.font_texture_property_name = font_texture_property_name;
        this.font_config_property_name = font_config_property_name;
        this.line_count_property_name = line_count_property_name;
    }

    private void LoadState(MaterialProperty encoding_texture_prop, MaterialEditor editor)
    {
        if (editor.targets.Length > 1)
        {
            cache_state = CachedState.None;
            gui_error = "Multi-editing is not supported";
            return;
        }

        Material material = (Material)editor.target;
        if (material != current_material_selection || cache_state == CachedState.None)
        {
            cache_state = CachedState.None;
            current_material_selection = material;

            // Shader sanity check, once per material change.
            // Shader modification forces reloading the drawer class for a recheck, no need to check for shader change.
            if (encoding_texture_prop.type != MaterialProperty.PropType.Texture) { gui_error = "[LereldarionCardTextLines(...)] must be applied to a Texture2D shader property"; return; }
            if (!material.HasTexture(font_texture_property_name)) { gui_error = "LereldarionCardTextLines(font_texture_property_name, _, _) must point to a Texture shader property"; return; }
            if (!material.HasVector(font_config_property_name)) { gui_error = "LereldarionCardTextLines(_, font_config_property_name, _) must point to an Vector shader property"; return; }
            if (!material.HasInteger(line_count_property_name)) { gui_error = "LereldarionCardTextLines(_, _, line_count_property_name) must point to an Integer shader property"; return; }

            cache_state = CachedState.SelectionAndShaderProperties;
        }

        if (cache_state >= CachedState.SelectionAndShaderProperties)
        {
            Texture font_texture = material.GetTexture(font_texture_property_name);
            if (current_font_texture != font_texture || cache_state == CachedState.SelectionAndShaderProperties)
            {
                cache_state = CachedState.SelectionAndShaderProperties;
                current_font_texture = font_texture;

                // Load metrics file
                if (font_texture is null) { gui_error = "Font texture is not defined"; return; }
                string font_texture_path = AssetDatabase.GetAssetPath(font_texture);
                if (font_texture_path is null || font_texture_path == "") { gui_error = "Font texture is not a valid asset"; return; }
                string metrics_path = System.IO.Path.ChangeExtension(font_texture_path, ".metrics.json");
                TextAsset metrics_json = AssetDatabase.LoadAssetAtPath<TextAsset>(metrics_path);
                if (metrics_json is null) { gui_error = $"Could not open font metrics file at '{metrics_path}'"; return; }

                // Load glyph data from JSON.
                MetricsJSON msdf_atlas_metrics = JsonUtility.FromJson<MetricsJSON>(metrics_json.text);
                if (msdf_atlas_metrics.glyphs.Length == 0) { gui_error = $"Could not parse font metrics from '{metrics_path}'"; return; }

                try { font = new Font(msdf_atlas_metrics); }
                catch (Exception e) { gui_error = $"Failed to load font {metrics_path}: {e.Message}"; }
                cache_state = CachedState.Font;
            }
        }

        if (cache_state >= CachedState.Font)
        {
            Texture encoding_texture = encoding_texture_prop.textureValue;
            if (current_encoding_texture != encoding_texture || cache_state == CachedState.Font)
            {
                cache_state = CachedState.Font;
                current_encoding_texture = encoding_texture;

                if (encoding_texture is not null) { current_encoding_texture_asset_path = AssetDatabase.GetAssetPath(encoding_texture); }
                line_cache = font.DecodeLines(encoding_texture);
                cache_state = CachedState.Text;
                gui_error = "";
            }
        }
    }

    public override void OnGUI(Rect rect, MaterialProperty encoding_texture_prop, string label, MaterialEditor editor)
    {
        Material material = (Material)editor.target;
        float line_height = base.GetPropertyHeight(encoding_texture_prop, label, editor);
        float line_spacing = line_height + 1;
        LoadState(encoding_texture_prop, editor);

        // Style
        Rect gui_full_line = new Rect(rect.x, rect.y, rect.width, line_height);
        GUIStyle style_label_centered = new GUIStyle(EditorStyles.label); style_label_centered.alignment = TextAnchor.MiddleCenter;
        GUIStyle invalid_text_field = StyleWithRedText(EditorStyles.textField);
        float numeric_field_width = 4.5f * line_height;

        // Section folding
        gui_section_foldout = EditorGUI.Foldout(gui_full_line, gui_section_foldout, label);
        if (!gui_section_foldout) { return; }
        gui_full_line.y += line_spacing;

        // Header line in case of error
        if (cache_state != CachedState.Text)
        {
            GUIStyle style = new GUIStyle(EditorStyles.label);
            style.normal.textColor = Color.red;
            EditorGUI.LabelField(gui_full_line, gui_error, style);
            return;
        }

        // Column positions
        Rect gui_list_button = new Rect(gui_full_line.x, gui_full_line.y, line_height, line_height);
        Rect gui_line_transform = new Rect(gui_list_button.xMax + 1, gui_full_line.y, 4 * numeric_field_width, line_height);
        Rect gui_line_inverted = new Rect(gui_line_transform.xMax + 1, gui_full_line.y, line_height, line_height);
        Rect gui_line_text = new Rect(gui_line_inverted.xMax + 1, gui_full_line.y, gui_full_line.xMax - gui_line_inverted.xMax, line_height);

        // Header line
        if (GUI.Button(gui_list_button, new GUIContent("+", "New line"))) { line_cache.AddLine(font); }
        EditorGUI.LabelField(
            new Rect(gui_line_transform.x, gui_line_transform.y, 2 * numeric_field_width, line_height),
            new GUIContent("Offset", "Offset in UV units"), style_label_centered);
        EditorGUI.LabelField(
            new Rect(gui_line_transform.x + 2 * numeric_field_width, gui_line_transform.y, numeric_field_width, line_height),
            new GUIContent("Size", "Font size, 1 = 1 vertical UV unit"), style_label_centered);
        EditorGUI.LabelField(
            new Rect(gui_line_transform.x + 3 * numeric_field_width, gui_line_transform.y, numeric_field_width, line_height),
            new GUIContent("Rotation", "Rotate text around bottom-left corner (degrees)"), style_label_centered);
        EditorGUI.LabelField(gui_line_inverted, new GUIContent("◙", "Make inverted text (box with character glyph holes)"), style_label_centered);
        EditorGUI.LabelField(gui_line_text, new GUIContent("Text", "Line turns red if it contains characters not supported by the font"), style_label_centered);

        GUIStyle save_button_style = line_cache.AllRepresentable() ? GUI.skin.button : StyleWithRedText(GUI.skin.button);
        bool gui_save_button = GUI.Button(
            new Rect(gui_line_text.x, gui_full_line.y, 0.2f * gui_line_text.width, line_height),
            new GUIContent("Save", "Write current text lines to encodings texture"), save_button_style);
        if (gui_save_button && line_cache.AllRepresentable())
        {
            var (encoding_texture, line_count) = font.EncodeLines(line_cache);

            // Manage asset database. Try to keep the asset_path cached even if temporarily deleted.
            if (encoding_texture is null)
            {
                if (current_encoding_texture is not null && AssetDatabase.Contains(current_encoding_texture))
                {
                    AssetDatabase.DeleteAsset(current_encoding_texture_asset_path);
                }
            }
            else
            {
                if (current_encoding_texture_asset_path is null || current_encoding_texture_asset_path == "")
                {
                    current_encoding_texture_asset_path = System.IO.Path.ChangeExtension(AssetDatabase.GetAssetPath(editor.target), $".{encoding_texture_prop.name}.asset");
                }
                AssetDatabase.CreateAsset(encoding_texture, current_encoding_texture_asset_path);
            }

            material.SetVector(font_config_property_name, new Vector4(font.grid_cell_pixels.x, font.grid_cell_pixels.y, font.grid_dimensions.x, font.msdf_pixel_range));
            material.SetInteger(line_count_property_name, line_count);
            material.SetTexture(encoding_texture_prop.name, encoding_texture);

            current_encoding_texture = encoding_texture;
        }

        bool gui_reload_button = GUI.Button(
            new Rect(gui_line_text.x + 0.8f * gui_line_text.width, gui_full_line.y, 0.2f * gui_line_text.width, line_height),
            new GUIContent("Reload", "Discard changes and reload lines from encodings texture"));
        if (gui_reload_button)
        {
            line_cache = font.DecodeLines(current_encoding_texture);
        }

        // Lines of text metadata
        for (int i = 0; i < line_cache.lines.Count; i += 1)
        {
            gui_list_button.y += line_spacing;
            gui_line_transform.y += line_spacing;
            gui_line_inverted.y += line_spacing;
            gui_line_text.y += line_spacing;
            if (GUI.Button(gui_list_button, new GUIContent("x", "Delete line")))
            {
                line_cache.RemoveLine(i);
                i -= 1; // Fix iteration count
            }
            else
            {
                // Combine position & size to use the ergonomic Vector4Field GUI element : XYZW labels allow changing value smoothly with mouse.
                line_cache.lines[i].transform = EditorGUI.Vector4Field(gui_line_transform, GUIContent.none, line_cache.lines[i].transform);
                line_cache.lines[i].inverted = EditorGUI.Toggle(gui_line_inverted, line_cache.lines[i].inverted);
                GUIStyle style = line_cache.lines[i].representable ? EditorStyles.textField : invalid_text_field;
                line_cache.SetLineText(i, EditorGUI.TextField(gui_line_text, line_cache.lines[i].text, style), font);
            }
        }
    }

    public override float GetPropertyHeight(MaterialProperty prop, string label, MaterialEditor editor)
    {
        float line_height = base.GetPropertyHeight(prop, label, editor);
        float line_spacing = line_height + 1;
        LoadState(prop, editor);

        int text_line_count = cache_state == CachedState.Text ? line_cache.lines.Count : 0;
        return line_spacing * (1 + (gui_section_foldout ? 1 + text_line_count : 0));
    }

    private class LineCache
    {
        public List<Line> lines;
        private bool? all_lines_representable = null;

        public LineCache()
        {
            lines = new List<Line>();
            all_lines_representable = true;
        }
        public void AddLine(Font font)
        {
            Line line = new Line();
            if (lines.Count > 0)
            {
                // Copy transform 1 line height lower for convenience
                line.transform = lines.Last().transform;
                line.NextLine(font);
            }
            lines.Add(line);
        }
        public void RemoveLine(int i)
        {
            lines.RemoveAt(i);
            all_lines_representable = null;
        }
        public void SetLineText(int i, string text, Font font)
        {
            if (lines[i].text != text)
            {
                lines[i].text = text;
                lines[i].representable = font.IsRepresentable(text);
                all_lines_representable = null;
            }
        }
        public bool AllRepresentable()
        {
            if (all_lines_representable is null) { all_lines_representable = lines.All(line => line.representable); }
            return (bool)all_lines_representable;
        }

        public class Line
        {
            public Vector4 transform = new Vector4(0, 0, 1, 0); // Offset.xy, FontSize, Rotation(deg)
            public string text = "";
            public bool inverted = false;

            public bool representable = true;

            public Vector4 TransformToShaderFormat(Font font)
            {
                float scale = font.font_ascender_pixels / transform.z; // At size 1, 1uvy * scale = font_ascender_pixels.
                float baseline_offset = -font.baseline_pixels / scale;
                float rotation_radians = transform.w * Mathf.PI / 180f;
                return new Vector4(
                    transform.x, transform.y + baseline_offset,
                    scale * Mathf.Cos(rotation_radians), scale * Mathf.Sin(rotation_radians));
            }
            public void SetTransformFromShaderFormat(Vector4 shader_transform, Font font)
            {
                float scale = new Vector2(shader_transform.z, shader_transform.w).magnitude;
                transform.z = font.font_ascender_pixels / scale;
                transform.x = shader_transform.x;
                float baseline_offset = -font.baseline_pixels / scale;
                transform.y = shader_transform.y - baseline_offset;
                transform.w = Mathf.Atan2(shader_transform.w, shader_transform.z) * 180f / Mathf.PI;
            }
            public void NextLine(Font font)
            {
                float y_shift_uv = -transform.z * font.line_height_as_ascender_ratio;
                float rotation_radians = transform.w * Mathf.PI / 180f;
                transform.x -= y_shift_uv * Mathf.Sin(rotation_radians);
                transform.y += y_shift_uv * Mathf.Cos(rotation_radians);
            }
        }
    }

    private class Font
    {
        // Using MSDF atlas in uniform grid mode.
        public readonly Vector2Int atlas_pixels;
        public readonly Vector2Int grid_dimensions;
        public readonly Vector2Int grid_cell_pixels;
        public readonly Vector2Int grid_cell_usable_pixels; // 0.5 pixel edges are unusable on glyphs to avoid bilinear blend with neighbor glyphs
        public readonly float msdf_pixel_range;
        // Glyph data. Glyph atlas id indexing is left to right, row by row from top to bottom.
        public readonly Glyph[] glyphs;
        public readonly Dictionary<char, uint> char_to_atlas_id;
        public readonly Dictionary<char, float> whitespace_advance_px;
        public readonly float font_ascender_pixels;
        public readonly float baseline_pixels;
        public readonly float line_height_as_ascender_ratio;
        public readonly Dictionary<(char, char), float> kerning_advance_px;
        public struct Glyph
        {
            // | left |advance |right-advance| Usable width of glyph.
            // |      |       .       |      |
            //      origin     `center         <- referential of EM
            // Center should always be the same, but recompute offset anyway.
            // Glyph width_unorm is size ratio of the center section
            public char character;
            public float advance_px;
            public float center_px;
            public uint width_unorm; // unorm encoded ratio of grid_cell_usable_pixels.x
        }

        public Font(MetricsJSON metrics)
        {
            // Basic grid dimensions
            atlas_pixels = new Vector2Int(metrics.atlas.width, metrics.atlas.height);
            grid_dimensions = new Vector2Int(metrics.atlas.grid.columns, metrics.atlas.grid.rows);
            grid_cell_pixels = new Vector2Int(metrics.atlas.grid.cellWidth, metrics.atlas.grid.cellHeight);
            grid_cell_usable_pixels = grid_cell_pixels - Vector2Int.one;
            msdf_pixel_range = metrics.atlas.distanceRange;
            if (atlas_pixels == Vector2Int.zero || grid_dimensions == Vector2Int.zero || grid_cell_pixels == Vector2Int.zero)
            {
                throw new Exception("msdf-atlas-gen font must be in uniform grid mode");
            }
            if (metrics.atlas.yOrigin != "bottom") { throw new Exception("msdf-atlas-gen font must be with y-origin=bottom"); }

            // Use pixel size as interchange. Cleaner than texture UVs that can be non-uniform if texture is a rectangle.
            // Scan glyphs to find the first with EM size data to use as model, as all glyphs share the same in uniform grid mode.
            Vector2 glyph_em_size = metrics.glyphs.Select(glyph => glyph.planeBounds.Size()).First(size => size != Vector2.zero);
            Vector2 em_to_pixel = ((Vector2)grid_cell_usable_pixels) / glyph_em_size;
            FloatToUNorm width_converter = new FloatToUNorm(bits_width, glyph_em_size.x);

            // Glyph pixel info
            font_ascender_pixels = em_to_pixel.y * metrics.metrics.ascender;
            baseline_pixels = em_to_pixel.y * metrics.atlas.grid.originY;
            line_height_as_ascender_ratio = metrics.metrics.lineHeight / metrics.metrics.ascender;

            glyphs = new Glyph[grid_dimensions.x * grid_dimensions.y];
            char_to_atlas_id = new Dictionary<char, uint>();
            whitespace_advance_px = new Dictionary<char, float>();
            foreach (var glyph in metrics.glyphs)
            {
                char character = (char)glyph.unicode;
                if (glyph.planeBounds.Size() == Vector2.zero)
                {
                    // Whitespace with no glyph
                    whitespace_advance_px.Add(character, em_to_pixel.x * glyph.advance);
                }
                else
                {
                    // Glyphs are seen in atlas id order, but recompute it anyway.
                    // This protects against holes (like space) and future packing change.
                    Vector2 atlas_glyph_center = glyph.atlasBounds.Center();
                    atlas_glyph_center.y = atlas_pixels.y - atlas_glyph_center.y; // put origin on top
                    Vector2Int cell_position = Vector2Int.FloorToInt(atlas_glyph_center / grid_cell_pixels);
                    int atlas_id = cell_position.y * grid_dimensions.x + cell_position.x;

                    // The V2 encoding requires a centered width. Advance is the glyph width but is not perfectly centered.
                    float glyph_em_center = glyph.planeBounds.Center().x;
                    float centered_width = 2 * Math.Max(glyph_em_center, glyph.advance - glyph_em_center);

                    glyphs[atlas_id] = new Glyph
                    {
                        character = character,
                        advance_px = em_to_pixel.x * glyph.advance,
                        center_px = em_to_pixel.x * glyph_em_center,
                        width_unorm = width_converter.Convert(centered_width),
                    };
                    char_to_atlas_id.Add(character, (uint)atlas_id);
                }
            }

            // Load kernings (advance delta between 2 chars)
            kerning_advance_px = metrics.kerning.ToDictionary(
                kerning => ((char)kerning.unicode1, (char)kerning.unicode2),
                kerning => kerning.advance * em_to_pixel.x);
        }

        public bool IsRepresentable(string text) { return text.All(c => char_to_atlas_id.ContainsKey(c) || whitespace_advance_px.ContainsKey(c)); }

        // Encode lines in a texture. Returns (texture, line_count). line_count == 0 => texture = null.
        // RGBA u32 texture, line per line.
        // First column is a control pixel for each line :
        // - RGBA[0..16] = f16 transform (offset, scaled rotation matrix coeffs)
        // - R[16..32] = f16 width_px, sign='-' if text inverted
        // - G[16..32] = u16 glyph count
        // Next pixels in the line : glyphs pixels, 4 by 4. (R->G->B->A)->(R->G->B->A) etc. Last one may have the last glyph repeated to pad.
        // Each pixel bit-encodes (atlas_id, width, center_x), the last 2 as Unorm ratios of respectively glyph_width and line_width.
        private const int bits_atlas_id = 12;
        private const int bits_width = 8; // Unorm ratio of glyph_width, resolution of 1/256
        private const int bits_center = 32 - (bits_width + bits_atlas_id); // Unorm ratio of line_width, resolution of 1/2^12
        public (Texture2D, int) EncodeLines(LineCache lines)
        {
            // Place characters one after another, computing center positions
            LineWithLayout[] layouted_lines = lines.lines.Select(line =>
            {
                float current_x = 0;
                char previous_c = (char)0;
                var layouted_glyphs = new List<LineWithLayout.Glyph>();
                foreach (char c in line.text)
                {
                    current_x += kerning_advance_px.GetValueOrDefault((previous_c, c));
                    previous_c = c;

                    if (whitespace_advance_px.TryGetValue(c, out float advance_px)) { current_x += advance_px; }
                    else
                    {
                        uint atlas_id = char_to_atlas_id[c];
                        var glyph = glyphs[atlas_id];
                        var entry = new LineWithLayout.Glyph { atlas_id = atlas_id, center_px = current_x + glyph.center_px };
                        current_x += glyph.advance_px;
                        layouted_glyphs.Add(entry);
                    }
                }

                layouted_glyphs.Sort((l, r) => l.center_px.CompareTo(r.center_px));
                return new LineWithLayout
                {
                    transform = line.TransformToShaderFormat(this),
                    inverted = line.inverted,
                    glyphs = layouted_glyphs,
                    width_px = current_x,
                };
            }).TakeWhile(line =>
            {
                // Ignore lines devoid of glyphs.
                return line.glyphs.Count > 0;
            }).ToArray();

            if (layouted_lines.Length == 0) { return (null, 0); }

            // Min resolution needed. We store 1 pixel for control, and 4 characters per foloowing pixel on a line.
            int max_glyph_count = layouted_lines.Max(line => line.glyphs.Count);
            int div_ceil(int n, int div) { return (n + div - 1) / div; }
            int encoding_resolution = Mathf.NextPowerOfTwo(div_ceil(max_glyph_count, 4) + 1 /*line config pixel*/);

            // Texture encoding. R32G32B32A32_UInt so use f32x4 and bitcast using load only. Stupid...
            Texture2D encodings = new Texture2D(encoding_resolution, Mathf.NextPowerOfTwo(layouted_lines.Length), GraphicsFormat.R32G32B32A32_SFloat, 1, TextureCreationFlags.None);
            NativeArray<uint> encoding_pixels = encodings.GetRawTextureData<uint>();
            for (int i = 0; i < layouted_lines.Length; i += 1)
            {
                NativeSlice<uint> line_pixels = encoding_pixels.Slice(4 * encodings.width * i, 4 * encodings.width);
                LineWithLayout line = layouted_lines[i];
                // Control pixel.
                line_pixels[0] = ((uint)Mathf.FloatToHalf(line.transform.x)) | (((uint)Mathf.FloatToHalf(line.width_px * (line.inverted ? -1 : 1))) << 16);
                line_pixels[1] = ((uint)Mathf.FloatToHalf(line.transform.y)) | (((uint)line.glyphs.Count) << 16);
                line_pixels[2] = (uint)Mathf.FloatToHalf(line.transform.z);
                line_pixels[3] = (uint)Mathf.FloatToHalf(line.transform.w);

                // Encode glyphs
                FloatToUNorm center_converter = new FloatToUNorm(bits_center, line.width_px);
                int offset = 4;
                foreach (var glyph in line.glyphs)
                {
                    line_pixels[offset++] = center_converter.Convert(glyph.center_px)
                        | (glyphs[glyph.atlas_id].width_unorm << bits_center)
                        | (glyph.atlas_id << (bits_center + bits_width));
                }

                // Padding with zero width glyph. Simplifies shader code, avoids handling a partial pixel.
                NativeSlice<uint> tail = line_pixels.Slice(offset);
                for (int j = 0; j < tail.Length; j += 1) { tail[j] = 0; }
            }
            for (int i = layouted_lines.Length; i < encodings.height; i += 1)
            {
                // Fill padding line config pixels with dummy values in case line_count variable is broken
                NativeSlice<uint> line_pixels = encoding_pixels.Slice(4 * encodings.width * i, 4 * encodings.width);
                for (int j = 0; j < 4; j += 1) { line_pixels[j] = 0; }

            }
            encodings.Apply();
            return (encodings, layouted_lines.Length);
        }
        private struct LineWithLayout
        {
            public Vector4 transform; // in shader format
            public bool inverted;
            public List<Glyph> glyphs;
            public float width_px;
            public struct Glyph
            {
                public uint atlas_id;
                public float center_px;
            }
        }

        public LineCache DecodeLines(Texture encodings)
        {
            var line_cache = new LineCache();
            if (encodings is null) { return line_cache; } // Empty

            if (!(encodings is Texture2D && encodings.graphicsFormat == GraphicsFormat.R32G32B32A32_SFloat)) { throw new Exception("Bad text encoding texture format"); }

            NativeArray<uint> encoding_pixels = ((Texture2D)encodings).GetRawTextureData<uint>();
            for (int i = 0; i < encodings.height; i += 1)
            {
                NativeSlice<uint> line_pixels = encoding_pixels.Slice(4 * encodings.width * i, 4 * encodings.width);
                if (line_pixels.Take(4).All(value => value == 0)) { break; } // Early end control pixel
                LineWithLayout layouted_line = new LineWithLayout();

                // Control pixel
                layouted_line.transform.x = Mathf.HalfToFloat((ushort)line_pixels[0]);
                layouted_line.transform.y = Mathf.HalfToFloat((ushort)line_pixels[1]);
                layouted_line.transform.z = Mathf.HalfToFloat((ushort)line_pixels[2]);
                layouted_line.transform.w = Mathf.HalfToFloat((ushort)line_pixels[3]);
                float line_width_px = Mathf.HalfToFloat((ushort)(line_pixels[0] >> 16));
                layouted_line.inverted = line_width_px < 0;
                line_width_px = Mathf.Abs(line_width_px);
                int glyph_count = (ushort)(line_pixels[1] >> 16);

                // TODO read chars. Track advance and add spaces in gaps.
                UNormToFloat center_converter = new UNormToFloat(bits_center, line_width_px);
                string text = "";
                char previous_c = (char)0;
                float current_x = 0;
                foreach (uint pixel in line_pixels.Slice(4, glyph_count))
                {
                    // Decode glyph atlas_id and line position. Ignore width, only useful for rendering as a bouding box.
                    float center_px = center_converter.Convert(pixel);
                    uint atlas_id = pixel >> (bits_center + bits_width);
                    Glyph glyph = glyphs[atlas_id];

                    current_x += kerning_advance_px.GetValueOrDefault((previous_c, glyph.character));
                    previous_c = glyph.character;

                    float glyph_origin_px = center_px - glyph.center_px;
                    text += EstimateWhitespace(glyph_origin_px - current_x);
                    text += glyph.character;
                    current_x = glyph_origin_px + glyph.advance_px;
                }
                text += EstimateWhitespace(line_width_px - current_x); // Trailing whitespace

                // Reconstructed line
                var line = new LineCache.Line();
                line.SetTransformFromShaderFormat(layouted_line.transform, this);
                line.inverted = layouted_line.inverted;
                line.text = text;
                line_cache.lines.Add(line);
            }
            return line_cache;
        }
        string EstimateWhitespace(float px)
        {
            // For simplicity, assume that a sequence of whitespace is of the same type.
            // Return the sequence of (space_type, count) that best fits the gap, with "" as default.
            // Detecting mixes would require solving a LP problem, and uses that require that are cursed anyway.
            string whitespace = "";
            float best_error = Mathf.Abs(px);
            foreach (var (space, advance_px) in whitespace_advance_px)
            {
                int count = Mathf.RoundToInt(px / advance_px);
                float error = Mathf.Abs(px - count * advance_px);
                if (count > 0 && error < best_error)
                {
                    best_error = error;
                    whitespace = new string(space, count);
                }
            }
            return whitespace;
        }
    }

    // Matches structure of https://github.com/Chlumsky/msdf-atlas-gen metrics JSON output in grid mode.
    [Serializable]
    private struct MetricsJSON
    {
        public Atlas atlas;
        public Metrics metrics;
        public Glyph[] glyphs;
        public Kerning[] kerning;

        [Serializable]
        public struct Atlas
        {
            // Pixel space
            public float distanceRange;
            public float size;
            public int width;
            public int height;
            public string yOrigin;
            public Grid grid;
            [Serializable]
            public struct Grid
            {
                public int cellWidth;
                public int cellHeight;
                public int columns;
                public int rows;
                public float originY; // EM space
            }
        }

        [Serializable]
        public struct Metrics
        {
            // EM space
            public float emSize;
            public float lineHeight;
            public float ascender;
            public float descender;
        }

        [Serializable]
        public struct Glyph
        {
            public int unicode;
            public float advance; // EM space
            // EM space.
            // Top & bottom same for all grid glyphs. top-bottom != lineHeight
            // |left| + advance/2 approximately conserved for all glyphs, at around 50% of |left| + right.
            // (-left, -bottom) is the origin point of the glyph in EM-space, advance the width.
            public Bounds planeBounds;
            public Bounds atlasBounds; // Pixel space

            [Serializable]
            public struct Bounds
            {
                public float left;
                public float bottom;
                public float right;
                public float top;
                public Vector2 Size() { return new Vector2(right - left, top - left); }
                public Vector2 Center() { return 0.5f * new Vector2(left + right, bottom + top); }
            }
        }
        [Serializable]
        public struct Kerning
        {
            public int unicode1;
            public int unicode2;
            public float advance; // EM
        }
    }

    static private GUIStyle StyleWithRedText(GUIStyle model)
    {
        var style = new GUIStyle(model);
        style.active.textColor = Color.red;
        style.focused.textColor = Color.red;
        style.hover.textColor = Color.red;
        style.normal.textColor = Color.red;
        style.onActive.textColor = Color.red;
        style.onFocused.textColor = Color.red;
        style.onHover.textColor = Color.red;
        style.onNormal.textColor = Color.red;
        return style;
    }

    private class FloatToUNorm
    {
        private readonly float range;
        private readonly float float_to_unorm;
        public FloatToUNorm(int bits, float range)
        {
            float_to_unorm = ((1u << bits) - 1u) / range;
            this.range = range;
        }
        public uint Convert(float v)
        {
            v = Mathf.Clamp(v, 0, range);
            return (uint)Mathf.RoundToInt(v * float_to_unorm);
        }
    }
    private class UNormToFloat
    {
        private readonly uint mask;
        private readonly float unorm_to_float;
        public UNormToFloat(int bits, float range)
        {
            mask = (1u << bits) - 1u;
            unorm_to_float = range / mask;
        }
        public float Convert(uint v) { return (v & mask) * unorm_to_float; }
    }
}

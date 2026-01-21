// Made by Lereldarion (https://github.com/lereldarion/)
// Free to redistribute under the MIT license
using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using System;
using BestHTTP.SecureProtocol.Org.BouncyCastle.Math.EC.Multiplier;

// Editor interface for text encoded to texture tables.
//
// Usage : tag a set of properties that encode text.
// [LereldarionTextLines(_Font_MSDF_Atlas_Texture, _Font_MSDF_Atlas_Config, _Text_LineCount)] _Text_Encoding_Texture("Text lines", 2D) = "" {}
// _Text_LineCount("Text line count", Integer) = 0
// _Font_MSDF_Atlas_Texture("Font texture (MSDF)", 2D) = "" {}
// _Font_MSDF_Atlas_Config("Font config", Vector) = (51, 46, 10, 2)
//
// _Text_Encoding_Texture texture asset (path/to/font_texture.<ext>) must have a companion path/to/font_texture.metrics.json with glyph metadata.
//
// Position is an offset in input UV.
// Size is set so that 1 input UV.y unit goes from font baseline to ascender height (https://en.wikipedia.org/wiki/Typeface#Font_metrics).
// Shader-side, a signed distance function at input UV scale is returned, with negative = interior of characters.
public class LereldarionTextLinesDrawer : MaterialPropertyDrawer
{
    // Values from shader property arguments
    private readonly string font_texture_property_name = null;
    private readonly string font_config_property_name = null;
    private readonly string line_count_property_name = null;

    // Caching state. Avoid reloading font metrics and text everytime.
    // Keep track of what was already validated or error.
    private CachedState cache_state = CachedState.None;
    private enum CachedState { None, SelectionAndShaderProperties, Font, Text }
    private UnityEngine.Object[] current_material_selection = null; // multi-editing not supported but track the selection for proper detection
    private Texture current_font_texture = null;
    private Font font = null;
    private Texture current_encoding_texture = null;
    private LineCache line_cache;

    // GUI state
    private bool gui_section_foldout = true;
    private string gui_error = "";

    public LereldarionTextLinesDrawer(string font_texture_property_name, string font_config_property_name, string line_count_property_name)
    {
        // Unity already splits by ',' and trims whitespace
        this.font_texture_property_name = font_texture_property_name;
        this.font_config_property_name = font_config_property_name;
        this.line_count_property_name = line_count_property_name;
    }

    private void LoadState(MaterialProperty text_prop, MaterialEditor editor)
    {
        Material material = (Material)editor.target;

        if (editor.targets != current_material_selection)
        {
            cache_state = CachedState.None;
            current_material_selection = editor.targets;

            if (editor.targets.Length > 1) { gui_error = "Multi-editing is not supported"; return; }

            // Shader sanity check, once per material change.
            // Shader modification forces reloading the drawer class for a recheck, no need to check for shader change.
            if (text_prop.type != MaterialProperty.PropType.Texture) { gui_error = "[LereldarionTextLines(...)] must be applied to a Texture2D shader property"; return; }
            if (!material.HasTexture(font_texture_property_name)) { gui_error = "LereldarionTextLines(font_texture_property_name, _, _) must point to a Texture shader property"; return; }
            if (!material.HasVector(font_config_property_name)) { gui_error = "LereldarionTextLines(_, font_config_property_name, _) must point to an Vector shader property"; return; }
            if (!material.HasInteger(line_count_property_name)) { gui_error = "LereldarionTextLines(_, _, line_count_property_name) must point to an Integer shader property"; return; }

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
            Texture encoding_texture = text_prop.textureValue;
            if (current_encoding_texture != encoding_texture || cache_state == CachedState.Font)
            {
                cache_state = CachedState.Font;
                current_encoding_texture = encoding_texture;

                line_cache = new LineCache(encoding_texture, font);
                cache_state = CachedState.Text;
                gui_error = "";
            }
        }
    }

    public override void OnGUI(Rect rect, MaterialProperty prop, string label, MaterialEditor editor)
    {
        float line_height = base.GetPropertyHeight(prop, label, editor);
        float line_spacing = line_height + 1;
        LoadState(prop, editor);

        // Style
        Rect gui_full_line = new Rect(rect.x, rect.y, rect.width, line_height);
        GUIStyle style_label_centered = new GUIStyle(EditorStyles.label); style_label_centered.alignment = TextAnchor.MiddleCenter;
        GUIStyle invalid_text_field = StyleWithRedText(EditorStyles.textField);
        float numeric_field_width = 4 * line_height;

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
        Rect gui_line_position_size = new Rect(gui_list_button.xMax + 1, gui_full_line.y, 3 * numeric_field_width, line_height);
        Rect gui_line_text = new Rect(gui_line_position_size.xMax + 1, gui_full_line.y, gui_full_line.xMax - gui_line_position_size.xMax, line_height);

        // Header line
        if (GUI.Button(gui_list_button, "+")) { line_cache.Add(); }
        EditorGUI.LabelField(new Rect(gui_line_position_size.x, gui_line_position_size.y, 2 * numeric_field_width, line_height), "Position", style_label_centered);
        EditorGUI.LabelField(new Rect(gui_line_position_size.x + 2 * numeric_field_width, gui_line_position_size.y, numeric_field_width, line_height), "Size", style_label_centered);
        EditorGUI.LabelField(gui_line_text, "Text", style_label_centered);
        if (line_cache.Dirty)
        {
            bool all_lines_representable = line_cache.Lines.All(line => line.representable);
            GUIStyle style = all_lines_representable ? GUI.skin.button : StyleWithRedText(GUI.skin.button);
            if (GUI.Button(new Rect(gui_line_text.x, gui_full_line.y, 0.2f * gui_line_text.width, line_height), "Save", style) && all_lines_representable)
            {
                // TODO save. Gen texture AND set properties
            }
            if (GUI.Button(new Rect(gui_line_text.x + 0.8f * gui_line_text.width, gui_full_line.y, 0.2f * gui_line_text.width, line_height), "Reset"))
            {
                line_cache = new LineCache(current_encoding_texture, font);
            }
        }

        // Lines of text metadata
        for (int i = 0; i < line_cache.Lines.Count; i += 1)
        {
            gui_list_button.y += line_spacing;
            gui_line_position_size.y += line_spacing;
            gui_line_text.y += line_spacing;
            if (GUI.Button(gui_list_button, "x"))
            {
                line_cache.RemoveAt(i);
                i -= 1; // Fix iteration count
            }
            else
            {
                // Combine position & size to use the ergonomic Vector3Field GUI element : XYZ labels allow changing value smoothly with mouse.
                line_cache.SetLinePositionSize(i, EditorGUI.Vector3Field(gui_line_position_size, GUIContent.none, line_cache.Lines[i].PositionSize));
                GUIStyle style = line_cache.Lines[i].representable ? EditorStyles.textField : invalid_text_field;
                line_cache.SetLineText(i, EditorGUI.TextField(gui_line_text, line_cache.Lines[i].text, style));
            }
        }
    }

    public override float GetPropertyHeight(MaterialProperty prop, string label, MaterialEditor editor)
    {
        float line_height = base.GetPropertyHeight(prop, label, editor);
        float line_spacing = line_height + 1;
        LoadState(prop, editor);

        int text_line_count = cache_state == CachedState.Text ? line_cache.Lines.Count : 0;
        return line_spacing * (1 + (gui_section_foldout ? 1 + text_line_count : 0));
    }

    private class LineCache
    {
        // Cached state of text system. Read from texture, store to texture.
        private List<Line> lines;
        private bool dirty = false;
        private Font font;

        public List<Line> Lines { get { return lines; } }
        public bool Dirty { get { return dirty; } }

        public LineCache(Texture encoding, Font font)
        {
            lines = new List<Line>();
            dirty = false;
            this.font = font;
        }

        public void Add()
        {
            lines.Add(new Line());
            dirty = true;
        }
        public void RemoveAt(int i)
        {
            lines.RemoveAt(i);
            dirty = true;
        }
        public void SetLinePositionSize(int i, Vector3 position_size)
        {
            dirty = dirty || lines[i].PositionSize != position_size;
            lines[i].PositionSize = position_size;
        }
        public void SetLineText(int i, string text)
        {
            if (lines[i].text != text)
            {
                dirty = true;
                lines[i].text = text;
                lines[i].representable = font.IsRepresentable(text);
            }
        }

        public class Line
        {
            public Vector3 PositionSize = new Vector3(0, 0, 1);
            public float Size { get => PositionSize.z; }
            public Vector2 Position { get => PositionSize; }
            public string text = "";
            public bool representable = true;
        }
    }

    private class Font
    {
        // Using MSDF atlas in uniform grid mode.
        public readonly Vector2Int atlas_pixels;
        public readonly Vector2Int grid_dimensions;
        public readonly Vector2Int grid_cell_pixels;
        // Glyph data. Glyph atlas id indexing is left to right, row by row from top to bottom.
        public readonly Glyph[] glyphs;
        public readonly Dictionary<char, int> char_to_atlas_id;
        public readonly Dictionary<char, float> whitespace_advance_px;
        public readonly float font_ascender_pixels;
        public readonly float baseline_pixels;
        public struct Glyph
        {
            public char character;
            public float advance_px;
            public float left_px;
        }

        public Font(MetricsJSON metrics)
        {
            // Basic grid dimensions
            atlas_pixels = new Vector2Int(metrics.atlas.width, metrics.atlas.height);
            grid_dimensions = new Vector2Int(metrics.atlas.grid.columns, metrics.atlas.grid.rows);
            grid_cell_pixels = new Vector2Int(metrics.atlas.grid.cellWidth, metrics.atlas.grid.cellHeight);
            if (atlas_pixels == Vector2Int.zero || grid_dimensions == Vector2Int.zero || grid_cell_pixels == Vector2Int.zero)
            {
                throw new Exception("msdf-atlas-gen font must be in uniform grid mode");
            }
            if (metrics.atlas.yOrigin != "bottom") { throw new Exception("msdf-atlas-gen font must be with y-origin=bottom"); }

            // Use pixel size as interchange. Cleaner than texture UVs that can be non-uniform if texture is a rectangle.
            // Scan glyphs to find the first with EM size data to use as model, as all glyphs share the same in uniform grid mode.
            Vector2 glyph_em_size = metrics.glyphs.Select(glyph => glyph.planeBounds.Size()).First(size => size != Vector2.zero);
            // A glyph is represented by (grid_cell_size-1) pixels, due to 0.5 pixel borders
            float em_to_pixel = (grid_cell_pixels.y - 1) / glyph_em_size.y;

            // Glyph pixel info
            font_ascender_pixels = em_to_pixel * metrics.metrics.ascender;
            baseline_pixels = em_to_pixel * metrics.atlas.grid.originY;

            glyphs = new Glyph[grid_dimensions.x * grid_dimensions.y];
            char_to_atlas_id = new Dictionary<char, int>();
            whitespace_advance_px = new Dictionary<char, float>();
            foreach (var glyph in metrics.glyphs)
            {
                char character = (char)glyph.unicode;
                if (glyph.planeBounds.Size() == Vector2.zero)
                {
                    // Whitespace with no glyph
                    whitespace_advance_px.Add(character, em_to_pixel * glyph.advance);
                }
                else
                {
                    // Glyphs are seen in atlas id order, but recompute it anyway.
                    // This protects against holes (like space) and future packing change.
                    Vector2 atlas_glyph_center = glyph.atlasBounds.Center();
                    atlas_glyph_center.y = atlas_pixels.y - atlas_glyph_center.y; // put origin on top
                    Vector2Int cell_position = Vector2Int.FloorToInt(atlas_glyph_center / grid_cell_pixels);
                    int atlas_id = cell_position.y * grid_dimensions.x + cell_position.x;

                    glyphs[atlas_id] = new Glyph
                    {
                        character = character,
                        advance_px = em_to_pixel * glyph.advance,
                        left_px = em_to_pixel * glyph.planeBounds.left
                    };
                    char_to_atlas_id.Add(character, atlas_id);
                }
            }
        }

        public bool IsRepresentable(string text) { return text.All(c => char_to_atlas_id.ContainsKey(c) || whitespace_advance_px.ContainsKey(c)); }
    }

    // Matches structure of https://github.com/Chlumsky/msdf-atlas-gen metrics JSON output in grid mode.
    [Serializable]
    private struct MetricsJSON
    {
        public Atlas atlas;
        public Metrics metrics;
        public Glyph[] glyphs;

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
}

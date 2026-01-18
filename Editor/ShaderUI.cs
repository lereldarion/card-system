using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;

// Usage : tag a set of properties that encode text.
//
// [LereldarionTextLines(_Font_MSDF_Atlas_Texture, _Font_MSDF_Atlas_Config, _Text_LineCount)] _Text_Encoding_Texture("Text lines", 2D) = "" {}
// _Text_LineCount("Text line count", Integer) = 0
// _Font_MSDF_Atlas_Texture("Font texture (MSDF)", 2D) = "" {}
// _Font_MSDF_Atlas_Config("Font config", Vector) = (51, 46, 10, 2)
//
// _Text_Encoding_Texture texture asset (path/to/font_texture.<ext>) must have a companion path/to/font_texture.metrics.json with glyph metadata.

// Does not seem to work within a namespace. Use prefix instead for name collision avoidance.
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
    private Object[] current_material_selection = null; // multi-editing not supported but track the selection for proper detection
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

                font = new Font(msdf_atlas_metrics);
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
        GUIStyle invalid_text_field = new GUIStyle(EditorStyles.textField);
        invalid_text_field.active.textColor = Color.red;
        invalid_text_field.focused.textColor = Color.red;
        invalid_text_field.normal.textColor = Color.red;
        invalid_text_field.onActive.textColor = Color.red;
        invalid_text_field.onFocused.textColor = Color.red;
        invalid_text_field.onHover.textColor = Color.red;
        invalid_text_field.onNormal.textColor = Color.red;
        float numeric_field_width = 3 * line_height;

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
        Rect gui_line_size = new Rect(gui_list_button.xMax + 1, gui_full_line.y, numeric_field_width, line_height);
        Rect gui_line_position = new Rect(gui_line_size.xMax + 1, gui_full_line.y, 2.5f * numeric_field_width, line_height);
        Rect gui_line_text = new Rect(gui_line_position.xMax + 1, gui_full_line.y, gui_full_line.xMax - gui_line_position.xMax, line_height);

        // Header line
        if (GUI.Button(gui_list_button, "+")) { line_cache.Add(); }
        EditorGUI.LabelField(gui_line_size, "Size", style_label_centered);
        EditorGUI.LabelField(gui_line_position, "Position", style_label_centered);
        EditorGUI.LabelField(gui_line_text, "Text", style_label_centered);
        if (line_cache.Dirty)
        {
            if (GUI.Button(new Rect(gui_line_text.x, gui_full_line.y, 0.2f * gui_line_text.width, line_height), "Save"))
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
            gui_line_size.y += line_spacing;
            gui_line_position.y += line_spacing;
            gui_line_text.y += line_spacing;
            if (GUI.Button(gui_list_button, "x"))
            {
                line_cache.RemoveAt(i);
                i -= 1; // Fix iteration count
            }
            else
            {
                line_cache.SetLineSize(i, EditorGUI.FloatField(gui_line_size, line_cache.Lines[i].Size));
                line_cache.SetLinePosition(i, EditorGUI.Vector2Field(gui_line_position, GUIContent.none, line_cache.Lines[i].Position));
                GUIStyle style = font.IsRepresentable(line_cache.Lines[i].Text) ? EditorStyles.textField : invalid_text_field;
                line_cache.SetLineText(i, EditorGUI.TextField(gui_line_text, line_cache.Lines[i].Text, style));
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

        public List<Line> Lines { get { return lines; } }
        public bool Dirty { get { return dirty; } }

        public LineCache(Texture encoding, Font font)
        {
            lines = new List<Line>();
            dirty = false;
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
        public void SetLineSize(int i, float size)
        {
            dirty = dirty || !Mathf.Approximately(lines[i].Size, size);
            lines[i].Size = size;
        }
        public void SetLinePosition(int i, Vector2 position)
        {
            dirty = dirty || lines[i].Position != position;
            lines[i].Position = position;
        }
        public void SetLineText(int i, string text)
        {
            dirty = dirty || lines[i].Text != text;
            lines[i].Text = text;
        }

        public class Line
        {
            public float Size = 1;
            public Vector2 Position = Vector2.zero;
            public string Text = "";
        }
    }

    private class Font
    {
        // In the texture, index is left to right, row by row from top to bottom.
        public readonly List<Glyph> glyphs;
        public readonly Dictionary<char, int> char_to_glyph;

        public struct Glyph
        {
            public char character;
        }

        public Font(MetricsJSON metrics)
        {
            glyphs = new List<Glyph>();
            char_to_glyph = new Dictionary<char, int>();
            for (int i = 0; i < metrics.glyphs.Length; i += 1)
            {
                var glyph = metrics.glyphs[i];
                glyphs.Add(new Glyph { character = (char)glyph.unicode });
                char_to_glyph.Add((char)glyph.unicode, i);
                // TODO useful metrics
            }
        }

        public bool IsRepresentable(string text) { return text.All(c => char_to_glyph.ContainsKey(c)); }
    }

    // Matches structure of https://github.com/Chlumsky/msdf-atlas-gen metrics JSON output in grid mode.
    [System.Serializable]
    private struct MetricsJSON
    {
        public Atlas atlas;
        public Metrics metrics;
        public Glyph[] glyphs;

        [System.Serializable]
        public struct Atlas
        {
            // Pixel space
            public float distanceRange;
            public float size;
            public float width;
            public float height;
            public Grid grid;

            [System.Serializable]
            public struct Grid
            {
                public float cellWidth;
                public float cellHeight;
                public int columns;
                public int rows;
                public float originY; // EM space ?
            }
        }

        [System.Serializable]
        public struct Metrics
        {
            // EM space
            public float emSize;
            public float lineHeight;
            public float ascender;
            public float descender;
        }

        [System.Serializable]
        public struct Glyph
        {
            public int unicode;
            public float advance; // EM space
            public Bounds planeBounds; // EM space
            public Bounds atlasBounds; // Pixel space

            [System.Serializable]
            public struct Bounds
            {
                public float left;
                public float bottom;
                public float right;
                public float top;
            }
        }
    }
}

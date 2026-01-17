using UnityEditor;
using UnityEngine;
using System.Collections.Generic;

// Usage : tag properties that encode text.
// [LereldarionTextLines(_Text_LineCount, _FontTex)] _Text("Text", 2D) = "" {}
// _Text_LineCount("Text line count", Integer) = 0 // usually hidden with [HideInInspector]
// _FontTex("Font (MSDF)", 2D) = "" {}
//
// _FontTex texture asset (path/to/font_texture.<ext>) must have a companion path/to/font_texture.metrics.json with glyph metadata.

// Does not seem to work within a namespace. Use prefix instead for name collision avoidance.
public class LereldarionTextLinesDrawer : MaterialPropertyDrawer
{
    // Values from shader property arguments
    private readonly string line_count_property_name = null;
    private readonly string font_texture_property_name = null;

    // GUI state
    private bool gui_section_foldout = true;
    private string gui_error = null;

    // Cache keys : keep track of the configuration the drawer looks at, to detect change and reload the cached data.
    private Object[] current_material_array = null; // multi-editing not supported but track the selection for proper detection
    private Texture current_font_texture = null;

    // Cached font metrics.
    private Dictionary<char, Glyph> font_metrics_cache = null;
    private struct Glyph
    {

    };

    // Cached state of text system. Read from texture, store to texture.
    private List<Line> text_lines_cache = null;
    private bool text_lines_cache_dirty = true;
    private class Line
    {
        public float Size = 1;
        public Vector2 Position = Vector2.zero;
        public string Text;
    };


    public LereldarionTextLinesDrawer(string line_count_property_name, string font_texture_property_name)
    {
        // Unity already splits by ',' and trims whitespace
        this.line_count_property_name = line_count_property_name;
        this.font_texture_property_name = font_texture_property_name;
    }

    private void LoadState(MaterialProperty text_prop, MaterialEditor editor)
    {
        if (editor.targets != current_material_array)
        {
            current_material_array = editor.targets;

            // Flush cached data due to selection change
            gui_error = null;

            text_lines_cache = null;

            current_font_texture = null;
            font_metrics_cache = null;
        }

        if (editor.targets.Length > 1) { gui_error = "Multi-editing is not supported"; return; }

        // Shader sanity check. Shader modification should force reloading the drawer class, no need to invalidate cache manually.
        Material material = (Material)editor.target;
        if (text_prop.type != MaterialProperty.PropType.Texture) { gui_error = "[LereldarionTextLines(...)] must be applied to a Texture2D shader property"; return; }
        if (!material.HasInteger(line_count_property_name)) { gui_error = "LereldarionTextLines(line_count_property, ...) must point to an Integer shader property"; return; }
        if (!material.HasTexture(font_texture_property_name)) { gui_error = "LereldarionTextLines(..., font_texture_property) must point to a Texture shader property"; return; }

        // Font metrics cache
        Texture font_texture = material.GetTexture(font_texture_property_name);
        if (font_texture != current_font_texture)
        {
            current_font_texture = font_texture;
            font_metrics_cache = null;
            text_lines_cache = null;

            string font_texture_path = AssetDatabase.GetAssetPath(font_texture);
            Debug.Log($"Font asset path '{font_texture_path}'");

            // TODO load TextAsset json
        }

        if (text_lines_cache is null)
        {
            // Load text lines from texture TODO
            text_lines_cache = new List<Line>();
            text_lines_cache_dirty = false;
        }
    }

    public override void OnGUI(Rect rect, MaterialProperty prop, string label, MaterialEditor editor)
    {
        float line_height = base.GetPropertyHeight(prop, label, editor);
        float line_spacing = line_height + 1;
        LoadState(prop, editor);

        // Style
        Rect gui_full_line = new Rect(rect.x, rect.y, rect.width, line_height);
        GUIStyle style_centered = new GUIStyle(EditorStyles.label); style_centered.alignment = TextAnchor.MiddleCenter;
        GUIStyle style_error = new GUIStyle(style_centered); style_error.richText = true;
        float numeric_field_width = 3 * line_height;

        // Section folding
        gui_section_foldout = EditorGUI.Foldout(gui_full_line, gui_section_foldout, label);
        if (!gui_section_foldout) { return; }
        gui_full_line.y += line_spacing;

        // Header line in case of error
        if (gui_error is not null)
        {
            EditorGUI.LabelField(gui_full_line, $"<color=red>{gui_error}</color>", style_error);
            return;
        }

        // Column positions
        Rect gui_list_button = new Rect(gui_full_line.x, gui_full_line.y, line_height, line_height);
        Rect gui_line_size = new Rect(gui_list_button.xMax + 1, gui_full_line.y, numeric_field_width, line_height);
        Rect gui_line_position = new Rect(gui_line_size.xMax + 1, gui_full_line.y, 2.5f * numeric_field_width, line_height);
        Rect gui_line_text = new Rect(gui_line_position.xMax + 1, gui_full_line.y, gui_full_line.xMax - gui_line_position.xMax, line_height);

        // Header line
        if (GUI.Button(gui_list_button, "+"))
        {
            text_lines_cache.Add(new Line());
            text_lines_cache_dirty = true;
        }
        EditorGUI.LabelField(gui_line_size, "Size", style_centered);
        EditorGUI.LabelField(gui_line_position, "Position", style_centered);
        EditorGUI.LabelField(gui_line_text, "Text", style_centered);
        if (text_lines_cache_dirty)
        {
            if (GUI.Button(new Rect(gui_line_text.x, gui_full_line.y, 0.2f * gui_line_text.width, line_height), "Save"))
            {
                // TODO save
            }
            if (GUI.Button(new Rect(gui_line_text.x + 0.8f * gui_line_text.width, gui_full_line.y, 0.2f * gui_line_text.width, line_height), "Reset"))
            {
                // TODO reset
            }
        }

        // Lines of text metadata
        for (int i = 0; i < text_lines_cache.Count; i += 1)
        {
            gui_list_button.y += line_spacing;
            gui_line_size.y += line_spacing;
            gui_line_position.y += line_spacing;
            gui_line_text.y += line_spacing;
            if (GUI.Button(gui_list_button, "x"))
            {
                text_lines_cache.RemoveAt(i);
                text_lines_cache_dirty = true;
                i -= 1; // Fix iteration count
            }
            else
            {
                float new_size = EditorGUI.FloatField(gui_line_size, text_lines_cache[i].Size);
                if (!Mathf.Approximately(new_size, text_lines_cache[i].Size)) { text_lines_cache_dirty = true; }
                text_lines_cache[i].Size = new_size;

                Vector2 new_position = EditorGUI.Vector2Field(gui_line_position, GUIContent.none, text_lines_cache[i].Position);
                if (new_position != text_lines_cache[i].Position) { text_lines_cache_dirty = true; }
                text_lines_cache[i].Position = new_position;

                string new_text = EditorGUI.TextField(gui_line_text, text_lines_cache[i].Text);
                if (new_text != text_lines_cache[i].Text) { text_lines_cache_dirty = true; }
                text_lines_cache[i].Text = new_text;
            }
        }
    }

    public override float GetPropertyHeight(MaterialProperty prop, string label, MaterialEditor editor)
    {
        float line_height = base.GetPropertyHeight(prop, label, editor);
        float line_spacing = line_height + 1;
        LoadState(prop, editor);

        int text_line_count = text_lines_cache is not null ? text_lines_cache.Count : 0;
        return line_spacing * (1 + (gui_section_foldout ? 1 + text_line_count : 0));
    }
}


using UnityEditor;
using UnityEngine;
using System.Collections.Generic;

// Use case : tag a pair of properties that encode text.
// [LereldarionTextLines(_Text_LineCount)] _Text("Text", 2D) = "" {}
// [HideInInspector] _Text_LineCount("Text line count", Integer) = 0

// Does not seem to work within a namespace. Use prefix instead for name collision avoidance.
public class LereldarionTextLinesDrawer : MaterialPropertyDrawer
{
    // Values from shader property arguments
    private string line_count_property_name;

    // Keep track of which material editor targets have been cached.
    // Currently only one material can be selected at a time (multi-editing not allowed), but we keep track of the material set to detect selection change.
    private Object[] current_material_array = null;

    // Cached state of text system. Read from texture, store to texture.
    private class Line
    {
        public float Size = 1;
        public Vector2 Position = Vector2.zero;
        public string Text;
    };
    private List<Line> text_lines = null; // if null, error state
    private bool cached_state_dirty = true;

    // GUI state
    private bool text_config_foldout = true;


    public LereldarionTextLinesDrawer(string line_count_property_name_)
    {
        line_count_property_name = line_count_property_name_;
    }

    private void LoadState(MaterialProperty text_prop, MaterialEditor editor)
    {
        // Do nothing if tracked materials are already cached.
        if (editor.targets == current_material_array) { return; }

        current_material_array = editor.targets;
        Material material = (Material)editor.target;

        // Flush
        text_lines = null;
        cached_state_dirty = true;

        // Validate state
        if (editor.targets.Length > 1) { Debug.LogWarning("LereldarionTextLinesDrawer does not support multi-editing"); return; }
        if (text_prop.type != MaterialProperty.PropType.Texture) { Debug.LogError("LereldarionTextLinesDrawer must be applied to a texture 2D shader property", material.shader); return; }
        if (!material.HasInteger(line_count_property_name)) { Debug.LogError("LereldarionTextLinesDrawer argument must point to an Integer shader property (line count)", material.shader); return; }

        // Load props TODO
        cached_state_dirty = false;
        text_lines = new List<Line>();
    }

    public override void OnGUI(Rect rect, MaterialProperty prop, string label, MaterialEditor editor)
    {
        float line_height = base.GetPropertyHeight(prop, label, editor);
        float line_spacing = line_height + 1;
        LoadState(prop, editor);

        // Style
        Rect gui_full_line = new Rect(rect.x, rect.y, rect.width, line_height);
        GUIStyle centered = new GUIStyle(EditorStyles.label);
        centered.alignment = TextAnchor.MiddleCenter;
        float numeric_field_width = 3 * line_height;

        // Section folding
        text_config_foldout = EditorGUI.Foldout(gui_full_line, text_config_foldout, label);
        if (!text_config_foldout) { return; }
        gui_full_line.y += line_spacing;

        // Header line in case of error
        if (text_lines is null)
        {
            EditorGUI.LabelField(gui_full_line, "Error, see logs", centered);
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
            text_lines.Add(new Line());
            cached_state_dirty = true;
        }
        EditorGUI.LabelField(gui_line_size, "Size", centered);
        EditorGUI.LabelField(gui_line_position, "Position", centered);
        EditorGUI.LabelField(gui_line_text, "Text", centered);
        if (cached_state_dirty)
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
        for (int i = 0; i < text_lines.Count; i += 1)
        {
            gui_list_button.y += line_spacing;
            gui_line_size.y += line_spacing;
            gui_line_position.y += line_spacing;
            gui_line_text.y += line_spacing;
            if (GUI.Button(gui_list_button, "x"))
            {
                text_lines.RemoveAt(i);
                cached_state_dirty = true;
                i -= 1; // Fix iteration count
            }
            else
            {
                float new_size = EditorGUI.FloatField(gui_line_size, text_lines[i].Size);
                if (!Mathf.Approximately(new_size, text_lines[i].Size)) { cached_state_dirty = true; }
                text_lines[i].Size = new_size;

                Vector2 new_position = EditorGUI.Vector2Field(gui_line_position, GUIContent.none, text_lines[i].Position);
                if (new_position != text_lines[i].Position) { cached_state_dirty = true; }
                text_lines[i].Position = new_position;

                string new_text = EditorGUI.TextField(gui_line_text, text_lines[i].Text);
                if(new_text != text_lines[i].Text) { cached_state_dirty = true; }
                text_lines[i].Text = new_text;
            }
        }
    }

    public override float GetPropertyHeight(MaterialProperty prop, string label, MaterialEditor editor)
    {
        float line_height = base.GetPropertyHeight(prop, label, editor);
        float line_spacing = line_height + 1;
        LoadState(prop, editor);

        int text_line_count = text_lines is not null ? text_lines.Count : 0;
        return line_spacing * (1 + (text_config_foldout ? 1 + text_line_count : 0));
    }
}


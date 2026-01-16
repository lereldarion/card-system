using UnityEditor;
using UnityEngine;
using System;
using System.Collections.Generic;

// Use case : tag a pair of properties that encode text.
// [LereldarionTextLines(_Text_LineCount)] _Text("Text", 2D) = "" {}
// [HideInInspector] _Text_LineCount("Text line count", Integer) = 0

// Does not seem to work within a namespace. Use prefix instead for name collision avoidance.
public class LereldarionTextLinesDrawer : MaterialPropertyDrawer
{
    private String line_count_property_name;

    private UnityEngine.Object[] cached_materials = null; // only one is cached, but track all to detect material set change
    private List<Line> text_lines = null; // if null, error state
    private bool properties_match_gui = false;

    private struct Line
    {
        float size;
        Vector2 position;
        string text;
    };

    public LereldarionTextLinesDrawer(String line_count_property_name_)
    {
        line_count_property_name = line_count_property_name_;
    }

    private void LoadState(MaterialProperty text_prop, MaterialEditor editor)
    {
        // Do nothing if tracked materials are already cached.
        if (editor.targets == cached_materials) { return; }

        cached_materials = editor.targets;
        Material material = (Material) editor.target;

        // Flush
        text_lines = null;
        properties_match_gui = false;

        // Validate state
        if (editor.targets.Length > 1) { Debug.LogWarning("LereldarionTextLinesDrawer does not support multi-editing"); return; }
        if (text_prop.type != MaterialProperty.PropType.Texture) { Debug.LogError("LereldarionTextLinesDrawer must be applied to a texture 2D shader property", material.shader); return; }
        if (!material.HasInteger(line_count_property_name)) { Debug.LogError("LereldarionTextLinesDrawer argument must point to an Integer shader property (line count)", material.shader); return; }

        // Load props TODO
        properties_match_gui = true;
        text_lines = new List<Line>();
    }

    public override void OnGUI(Rect position, MaterialProperty prop, String label, MaterialEditor editor)
    {
        float line_height = base.GetPropertyHeight(prop, label, editor);
        LoadState(prop, editor);

        if (text_lines is null)
        {
            position.height = line_height;
            EditorGUI.LabelField(position, label, "Error");
            return;
        }
    }

    public override float GetPropertyHeight(MaterialProperty prop, String label, MaterialEditor editor)
    {
        float line_height = base.GetPropertyHeight(prop, label, editor);
        LoadState(prop, editor);
        
        // One line for error state
        if (text_lines is null) { return line_height; }
        
        // TODO 
        return line_height * 10;
    }
}


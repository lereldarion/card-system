// Made by Lereldarion (https://github.com/lereldarion/)
// Free to redistribute under the MIT license

// Procedural card design for explorer members.
// Should be placed on a quad, with UVs in [0, W]x[0, 1]. UVs should not be distorded. W in [0, 1] is the aspect ratio.
// - Foreground / background textures are by default centered and using full height.
// - Recommended texture configuration : clamp + trilinear. Foreground with alpha, background without.
//
// Many properties are baked to constants now that the design is validated. The selectors are kept but commented out.

Shader "Lereldarion/Card/Explorer" {
    Properties {
        [MainTexture] _Foreground_Texture("Avatar (alpha cutout)", 2D) = "" {}
        _Background_Texture("Background (no alpha)", 2D) = "" {}
        _Avatar_Parallax_Depth("Avatar parallax depth", Range(0, 1)) = 0.01
        _Background_Parallax_Depth("Background parallax depth", Range(0, 1)) = 0.1

        // [Header(Card Shape)]
        // _Aspect_Ratio("Maximum UV width (aspect ratio of card quad)", Range(0, 1)) = 0.707
        // _Corner_Radius("Radius of corners", Range(0, 0.1)) = 0.024
        
        [Header(UI)]
        [MainColor] _UI_Color("Color", Color) = (1, 1, 1, 1)
        // _UI_Common_Margin("Common margin size", Range(0, 0.1)) = 0.03
        // _UI_Border_Thickness("Thickness of block borders", Range(0, 0.01)) = 0.0015
        // _UI_Outer_Border_Chamfer("Outer border chamfer", Range(0, 0.1)) = 0.04
        // _UI_Title_Height("Title box height", Range(0, 0.1)) = 0.036
        // _UI_Title_Chamfer("Title box chamfer", Range(0, 0.1)) = 0.023
        // _UI_Description_Height("Description box height", Range(0, 0.5)) = 0.15
        // _UI_Description_Chamfer("Description box chamfer", Range(0, 0.1)) = 0.034
        
        // [Header(Blurring effect)]
        // _Blur_Mip_Bias("Blur Mip bias", Range(-16, 16)) = 2
        // _Blur_Darken("Darken blurred areas", Range(0, 1)) = 0.3

        [Header(Logo)]
        _Logo_Color("Color", Color) = (1, 1, 1, 0.1)
        _Logo_Texture("Logo (MSDF)", 2D) = "" {}
        // _Logo_Rotation_Scale_Offset("Logo rotation, scale, offset", Vector) = (23, 0.41, 0.19, -0.097)
        // _Logo_MSDF_Pixel_Range("Logo MSDF pixel range", Float) = 8
        // _Logo_MSDF_Texture_Size("Logo MSDF texture size", Float) = 128
        _Logo_Back_Size("Card back logo size", Float) = 0.35

        [Header(Text)]
        [LereldarionCardTextLines(_Font_MSDF_Atlas_Texture, _Font_MSDF_Atlas_Config, _Text_LineCount)] _Text_Encoding_Texture("Text lines", 2D) = "" {}
        [HideInInspector] _Text_LineCount("Text line count", Integer) = 0
        [NoScaleOffset] _Font_MSDF_Atlas_Texture("Font texture (MSDF)", 2D) = "" {}
        [HideInInspector] _Font_MSDF_Atlas_Config("Font config", Vector) = (51, 46, 10, 2)
    }
    SubShader {
        Tags {
            "RenderType" = "Opaque"
            "Queue" = "AlphaTest"
            "PreviewType" = "Plane"
            "VRCFallback" = "Unlit"
        }

        Pass {
            Cull Off
            ZTest LEqual
            ZWrite On
            Blend Off

            CGPROGRAM
            #pragma warning (error : 3205) // implicit precision loss
            #pragma warning (error : 3206) // implicit truncation

            #pragma target 5.0
            #pragma multi_compile_instancing

            #pragma vertex vertex_stage
            #pragma fragment fragment_stage

            #include "UnityCG.cginc"

            struct VertexData {
                float3 position : POSITION;
                float3 normal : NORMAL;
                float4 tangent : TANGENT;
                float2 uv0 : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct FragmentInput {
                float4 position_cs : SV_POSITION;
                float3 position_ws : POSITION_WS;
                float3 normal_ws : NORMAL_WS;
                float4 tangent_ws : TANGENT_WS;
                float2 uv0 : UV0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // Common hardcoded sampler. Set by keywords in name https://docs.unity3d.com/Manual/SL-SamplerStates.html
            uniform SamplerState sampler_clamp_bilinear;
            
            uniform Texture2D<fixed4> _Foreground_Texture;
            uniform SamplerState sampler_Foreground_Texture;
            uniform float4 _Foreground_Texture_ST;
            uniform Texture2D<fixed3> _Background_Texture;
            uniform SamplerState sampler_Background_Texture;
            uniform float4 _Background_Texture_ST;
            uniform float _Avatar_Parallax_Depth;
            uniform float _Background_Parallax_Depth;

            static const float _Aspect_Ratio = 0.707;
            static const float _Corner_Radius = 0.024;
            
            uniform fixed4 _UI_Color;
            static const float _UI_Common_Margin = 0.03;
            static const float _UI_Border_Thickness = 0.0015;
            static const float _UI_Outer_Border_Chamfer = 0.04;
            static const float _UI_Title_Height = 0.036;
            static const float _UI_Title_Chamfer = 0.023;
            static const float _UI_Description_Height = 0.15;
            static const float _UI_Description_Chamfer = 0.034;

            static const float _Blur_Mip_Bias = 2;
            static const float _Blur_Darken = 0.3;

            uniform Texture2D<float3> _Logo_Texture;
            uniform fixed4 _Logo_Color;
            static const float4 _Logo_Rotation_Scale_Offset = float4(23, 0.41, 0.19, -0.097);
            static const float _Logo_MSDF_Pixel_Range = 8;
            static const float _Logo_MSDF_Texture_Size = 128;
            uniform float _Logo_Back_Size;

            uniform Texture2D<float4> _Text_Encoding_Texture; // uint4 but must use float4 due to unity refusing to create a u32x4 texture. Bitcast !
            uniform uint _Text_LineCount;
            uniform Texture2D<float3> _Font_MSDF_Atlas_Texture;
            uniform float4 _Font_MSDF_Atlas_Config; // (glyph_pixels.xy, atlas_columns, msdf_pixel_range)

            void vertex_stage(VertexData input, out FragmentInput output) {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.position_cs = UnityObjectToClipPos(input.position);
                output.position_ws = mul(unity_ObjectToWorld, float4(input.position, 1)).xyz;
                output.normal_ws = UnityObjectToWorldNormal(input.normal);
                output.tangent_ws.xyz = UnityObjectToWorldDir(input.tangent.xyz);
                output.tangent_ws.w = input.tangent.w * unity_WorldTransformParams.w;
                output.uv0 = input.uv0;
            }

            float length_sq(float2 v) { return dot(v, v); }
            float3 safe_normalize(float3 v) { return v * rsqrt(max(0.001f, dot(v, v))); }
            float2 pow2(float2 v) { return v * v; }
            
            // Inigo Quilez https://iquilezles.org/articles/distfunctions2d/. Use negative for interior.
            float extrude_border_with_thickness(float sdf, float thickness) {
                return abs(sdf) - thickness;
            }
            float psdf_chamfer_box(float2 p, float2 b, float chamfer) {
                // Pseudo SDF, with sharp corners. Useful to keep sharp corners when thickness is added.
                const float2 d = abs(p) - b;
                const float rectangle_sd = max(d.x, d.y);
                const float chamfer_sd = sqrt(0.5) * (d.x + d.y + chamfer);
                return max(rectangle_sd, chamfer_sd);
            }
            float psdf_half_chamfer_box(float2 p, float2 b, float chamfer) {
                const float2 d = abs(p) - b;
                const float rectangle_sd = max(d.x, d.y);
                const float chamfer_sd = sqrt(0.5) * (d.x + d.y + chamfer);
                return (p.x >= 0) != (p.y >= 0) ? max(rectangle_sd, chamfer_sd) : rectangle_sd;
            }
            
            // SDF anti-alias blend
            // https://blog.pkh.me/p/44-perfecting-anti-aliasing-on-signed-distance-functions.html
            // https://github.com/Chlumsky/msdfgen has other info.
            // Strategy : determine uv / sdf scale in screen space, and blend smoothly at 1px screen scale.
            // sdf should be in uv units, so both scales are equivalent. Use uv as it is continuous, sdf is not due to ifs.
            float compute_screenspace_scale_of_uv(float2 uv) {
                const float2 screenspace_uv_scales = sqrt(pow2(ddx_fine(uv)) + pow2(ddy_fine(uv)));
                return 0.5 * (screenspace_uv_scales.x + screenspace_uv_scales.y);
            }
            float sdf_blend_with_aa(float sdf, float screenspace_scale_of_uv) {
                const float w = 0.5 * screenspace_scale_of_uv;
                return smoothstep(-w, w, -sdf);
            }

            // MSDF textures utils https://github.com/Chlumsky/msdfgen
            float median(float3 msd) { return max(min(msd.r, msd.g), min(max(msd.r, msd.g), msd.b)); }
            float msdf_sample(Texture2D<float3> tex, float2 uv, float pixel_range, float2 texture_pixels) {
                const float tex_sd = median(tex.SampleLevel(sampler_clamp_bilinear, uv, 0)) - 0.5;

                // tex_sd is in [-0.5, 0.5]. It represents texture pixel ranges between [-pixel_range, pixel_range].
                const float texture_pixel_sd = tex_sd * 2 * pixel_range;
                const float texture_uv_sd = texture_pixel_sd / texture_pixels;
                return -texture_uv_sd; // MSDF tooling generates inverted SDF (positive inside)
            }

            float sdf_lereldarion_text_lines(float2 uv, Texture2D<float3> font_atlas, float4 font_config, Texture2D<float4> encodings, uint line_count) {
                const uint bits_atlas_id = 12; const uint bits_atlas_id_mask = (1u << bits_atlas_id) - 1u;
                const uint bits_width = 8; const uint bits_width_mask = (1u << bits_width) - 1u;
                const uint bits_center = 32 - (bits_atlas_id + bits_width); const uint bits_center_mask = (1u << bits_center) - 1u;

                uint2 encodings_pixels;
                encodings.GetDimensions(encodings_pixels.x, encodings_pixels.y);
                line_count = min(line_count, encodings_pixels.y); // Clamp line_count in case it is broken

                const float2 glyph_cell_pixels = font_config.xy;
                const float2 glyph_usable_pixels = glyph_cell_pixels - 1; // Index from pixel center to pixel center, avoid edges.
                const uint glyph_columns = (uint) font_config.z;
                const float msdf_pixel_range = font_config.w;
                
                float2 atlas_pixels;
                font_atlas.GetDimensions(atlas_pixels.x, atlas_pixels.y);
                const float2 atlas_pixel_to_uv = 1. / atlas_pixels;
                
                float sd = 100000; // Infinity
                for(uint i = 0; i < line_count; i += 1) {
                    // Decode control pixel.
                    const uint4 control = asuint(encodings[uint2(0, i)]);
                    const uint4 control_upper = control >> 16;
                    const float4 transform = f16tof32(control);
                    const float control_upper_r = f16tof32(control_upper.x);
                    const float line_width_px = abs(control_upper_r);
                    if(line_width_px == 0) { break; } // Fallback stop
                    const uint glyph_count = control_upper.y;

                    // Apply 2D transform to convert to line referential, in glyph pixel units.
                    // transform=(offset.xy, scale_cos, scale_sin)
                    const float2x2 scale_rotation = float2x2(transform.z, transform.w, -transform.w, transform.z);
                    const float2 line_px = mul(scale_rotation, uv - transform.xy);

                    // Text rectangle bounding box test.
                    if(all(0 <= line_px && line_px <= float2(line_width_px, glyph_usable_pixels.y))) {
                        // Scale for rescaling signed distance after sample. Sign of control_upper_r is inverted flag.
                        const float inverse_scale = sign(control_upper_r) / length(transform.zw);
                        // Glyph array : packed 4 per pixel. Start at 1 to leave space for control.
                        uint glyph_array_start = 1;
                        uint glyph_array_end = 1 + (glyph_count - 1) / 4;
                        // Do a linear search of which glyph matches line_px.x, if any. 4 by 4.
                        // Start search at position if glyphs were of equal sizes.
                        const float line_x_ratio = line_px.x / line_width_px; // [0, 1]
                        uint glyph_array_index = 1 + uint(line_x_ratio * (glyph_array_end - glyph_array_start));
                        while(true) {
                            // Decode pixel
                            uint4 pixel = asuint(encodings[uint2(glyph_array_index, i)]);
                            const float4 centers = float4(pixel & bits_center_mask) / float(bits_center_mask) * line_width_px;
                            pixel = pixel >> bits_center;
                            const float4 half_advances = 0.5 * float4(pixel & bits_width_mask) / float(bits_width_mask) * glyph_usable_pixels.x;
                            const uint4 atlas_ids = pixel >> bits_width;

                            const float4 glyph_x_px_centereds = line_px.x - centers;

                            // If line_px is not contained within the 4 glyphs of this pixel, do linear search
                            if(glyph_x_px_centereds[0] < -half_advances[0] && glyph_array_index > glyph_array_start) {
                                glyph_array_index -= 1;
                                glyph_array_end = glyph_array_index; // Prevent ping-pong
                                continue;
                            }
                            if(glyph_x_px_centereds[3] > half_advances[3] && glyph_array_index < glyph_array_end) {
                                glyph_array_index += 1;
                                glyph_array_start = glyph_array_index; // Prevent ping-pong
                                continue;
                            }

                            // line_px is in one of the 4 glyphs of this pixel, or out of bounds.
                            // search among the 4. unrolled to a sequence of movc
                            uint atlas_id = bits_atlas_id_mask + 1;
                            float glyph_x_px_centered = 0;
                            bool4 within_glyph = abs(glyph_x_px_centereds) < half_advances;
                            [unroll] for(uint j = 0; j < 4; j += 1) {
                                if(within_glyph[j]) {
                                    atlas_id = atlas_ids[j];
                                    glyph_x_px_centered = glyph_x_px_centereds[j];
                                }
                            }

                            if(atlas_id <= bits_atlas_id_mask) {
                                float2 glyph_px = float2(glyph_x_px_centered + 0.5 * glyph_usable_pixels.x, line_px.y);
                                const uint atlas_row = atlas_id / glyph_columns;
                                const uint atlas_column = atlas_id - atlas_row * glyph_columns;
                                const float2 atlas_offset_px = float2(atlas_column * glyph_cell_pixels.x, atlas_pixels.y - glyph_cell_pixels.y * (atlas_row + 1)) + 0.5;
                                const float tex_sd = median(font_atlas.SampleLevel(sampler_clamp_bilinear, (glyph_px + atlas_offset_px) * atlas_pixel_to_uv, 0)) - 0.5;
                                // tex_sd is in [-0.5, 0.5]. It represents texture pixel ranges between [-msdf_pixel_range, msdf_pixel_range], using the inverse SDF direction.
                                const float tex_sd_pixel = -tex_sd * 2 * msdf_pixel_range;
                                sd = min(sd, tex_sd_pixel * inverse_scale);
                            }
                            break;
                        }
                    }
                }
                return sd;
            }

            fixed4 fragment_stage(FragmentInput input, bool is_front_face : SV_IsFrontFace) : SV_Target {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                // Mesh info
                const float3 view_dir_ws = normalize(_WorldSpaceCameraPos - input.position_ws);
                const float3 bitangent_ws = safe_normalize(cross(input.tangent_ws.xyz, input.normal_ws) * input.tangent_ws.w * -1);
                const float3x3 tbn_matrix = float3x3(normalize(input.tangent_ws.xyz), bitangent_ws, input.normal_ws);
                const float3 view_dir_ts = mul(tbn_matrix, view_dir_ws);

                // UVs.
                const float2 raw_uv_range = float2(_Aspect_Ratio, 1);
                const float2 quadrant_size = 0.5 * raw_uv_range;
                const float2 centered_uv = input.uv0 - quadrant_size; // [-AR/2, AR/2] x [-0.5, 0.5]
                const float screenspace_scale_of_uv = compute_screenspace_scale_of_uv(input.uv0);
                
                bool blurred = false;
                float ui_sd;
                float logo_opacity = 0;

                // Round corners. Inigo Quilez SDF strategy, L2 distance to inner rectangle.
                if(length_sq(max(abs(centered_uv) - (quadrant_size - _Corner_Radius), 0)) > _Corner_Radius * _Corner_Radius) {
                     discard;
                }

                // Back of the card
                if(!is_front_face) {
                    // TODO improve this placeholder
                    const float sd = msdf_sample(_Logo_Texture, centered_uv / _Logo_Back_Size + 0.5, _Logo_MSDF_Pixel_Range, _Logo_MSDF_Texture_Size);
                    fixed3 color = lerp(0, _UI_Color.rgb, sdf_blend_with_aa(sd * _Logo_Back_Size, screenspace_scale_of_uv));
                    return fixed4(color, 1);
                }

                // Outer box
                const float border_box_sd = psdf_chamfer_box(centered_uv, quadrant_size - _UI_Common_Margin, _UI_Outer_Border_Chamfer);
                ui_sd = extrude_border_with_thickness(border_box_sd, _UI_Border_Thickness);

                // Border triangles gizmos
                if((centered_uv.x >= 0) == (centered_uv.y >= 0)) {
                    const float2 p = abs(centered_uv) - (quadrant_size - _Corner_Radius); // Corner at curved edge center
                    const float rectangle_edges_sd = max(p.x, p.y);
                    const float diag_axis = sqrt(0.5) * (p.x + p.y);
                    // Align outer triangle diagonal with inner chamfer
                    const float diag_offset_a = (_UI_Common_Margin + 0.5 * _UI_Outer_Border_Chamfer - _Corner_Radius) * sqrt(2) + _UI_Border_Thickness;
                    const float diag_offset_b = diag_offset_a - 6 * _UI_Border_Thickness;
                    const float diag_offset_c = diag_offset_b - 4 * _UI_Border_Thickness;

                    const float gizmo_sd = max(rectangle_edges_sd, min(max(-(diag_axis + diag_offset_a), diag_axis + diag_offset_b), -(diag_axis + diag_offset_c)));
                    ui_sd = min(ui_sd, gizmo_sd);
                }

                if(border_box_sd > 0) {
                    blurred = true;
                } else {
                    // Description
                    const float2 description_uv = centered_uv - float2(0, _UI_Description_Height + 2 * _UI_Common_Margin - quadrant_size.y);
                    const float2 description_size = float2(quadrant_size.x - 2 * _UI_Common_Margin, _UI_Description_Height);
                    const float description_box_sd = psdf_half_chamfer_box(description_uv, description_size, _UI_Description_Chamfer);
                    ui_sd = min(ui_sd, extrude_border_with_thickness(description_box_sd, _UI_Border_Thickness));

                    if(description_box_sd <= 0) {
                        // Description text
                        blurred = true;

                        // Logo
                        float2 logo_rotation_cos_sin;
                        sincos(_Logo_Rotation_Scale_Offset.x * UNITY_PI / 180.0, logo_rotation_cos_sin.y, logo_rotation_cos_sin.x);
                        logo_rotation_cos_sin /= _Logo_Rotation_Scale_Offset.y;
                        const float2x2 logo_rotscale = float2x2(logo_rotation_cos_sin.xy, logo_rotation_cos_sin.yx * float2(-1, 1));
                        const float sd = msdf_sample(_Logo_Texture, mul(logo_rotscale, description_uv - _Logo_Rotation_Scale_Offset.zw) + 0.5, _Logo_MSDF_Pixel_Range, _Logo_MSDF_Texture_Size);
                        logo_opacity = sdf_blend_with_aa(sd * _Logo_Rotation_Scale_Offset.y, screenspace_scale_of_uv);
                    } else {
                        // Title
                        const float2 title_uv = centered_uv - float2(0, quadrant_size.y - (_UI_Title_Height + 2 * _UI_Common_Margin));
                        const float2 title_size = float2(quadrant_size.x - 2 * _UI_Common_Margin, _UI_Title_Height);
                        const float title_box_sd = psdf_half_chamfer_box(title_uv, title_size, _UI_Title_Chamfer);
                        blurred = title_box_sd <= 0;
                        ui_sd = min(ui_sd, extrude_border_with_thickness(title_box_sd, _UI_Border_Thickness));
                    }
                }

                ui_sd = min(ui_sd, sdf_lereldarion_text_lines(input.uv0, _Font_MSDF_Atlas_Texture, _Font_MSDF_Atlas_Config, _Text_Encoding_Texture, _Text_LineCount));

                // Texture sampling with parallax.
                // Make tiling and offset values work on the center
                const float2 avatar_uv = (centered_uv + ParallaxOffset(-1, _Avatar_Parallax_Depth, view_dir_ts)) * _Foreground_Texture_ST.xy + 0.5 + _Foreground_Texture_ST.zw;
                const float2 background_uv = (centered_uv + ParallaxOffset(-1, _Background_Parallax_Depth, view_dir_ts)) * _Background_Texture_ST.xy + 0.5 + _Background_Texture_ST.zw;
                
                // Handle blurring with mip bias : use a blurrier mip than adequate.
                // This may fail from too close if biased mip is clamped to 0 anyway, but this seems ok for 1K / 2K textures at card scale.
                const float mip_bias = blurred ? _Blur_Mip_Bias : 0;
                const fixed4 foreground = _Foreground_Texture.SampleBias(sampler_Foreground_Texture, avatar_uv, mip_bias);
                const fixed3 background = _Background_Texture.SampleBias(sampler_Background_Texture, background_uv, mip_bias);

                // Color composition
                fixed3 color = lerp(background, foreground.rgb, foreground.a);
                if(blurred) { color = lerp(color, 0, _Blur_Darken); }
                color = lerp(color, _Logo_Color.rgb, logo_opacity * _Logo_Color.a);
                color = lerp(color, _UI_Color.rgb, sdf_blend_with_aa(ui_sd, screenspace_scale_of_uv));
                return fixed4(color, 1);
            }
            ENDCG            
        }
    }
}
Shader "PSX/Fog Scroll"
{
    // Transparent, unlit, double-sided fog-plane shader that scrolls its texture
    // over time to fake drifting fog. The scroll VELOCITY is taken straight from
    // the material's texture Offset (the X/Y under Tiling/Offset), so each plane
    // can be given its own drift direction and rate without touching the shader.
    // Tiling still works as tiling. Set an Offset of (0,0) and nothing moves.
    Properties
    {
        _Color        ("Tint (RGBA)", Color) = (1, 1, 1, 0.15)
        _MainTex      ("Fog Texture", 2D) = "white" {}

        _ScrollSpeed  ("Scroll Speed (multiplies Offset)", Float) = 1

        [Header(Second Layer)]
        [Toggle] _TwoLayer ("Cross-fade a second scrolling copy", Float) = 1
        _Layer2Scale  ("Layer 2 UV scale", Float) = 0.63
        _Layer2Speed  ("Layer 2 relative speed", Float) = -0.55
        _Distort      ("Layer 2 warps layer 1 by", Range(0, 0.25)) = 0.04

        [Header(Alpha)]
        [Toggle] _AlphaFromLum ("Alpha from texture brightness (for RGB fog maps)", Float) = 1
        _AlphaContrast ("Alpha contrast", Range(0.1, 6)) = 1
        _AlphaBoost    ("Alpha boost", Range(0, 4)) = 1
        _AlphaCutoff   ("Alpha cutoff (discard below)", Range(0, 1)) = 0

        [Header(PS1 Look)]
        _AffineWarp   ("Affine texture warp", Range(0, 1)) = 0.25
        _VertexSnap   ("Vertex snap", Range(0, 1)) = 0
        _SnapResolution ("Snap grid", Float) = 96

        [Header(Blending)]
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src Blend (default SrcAlpha)", Float) = 5
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst Blend (default OneMinusSrcAlpha)", Float) = 10
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "IgnoreProjector" = "True" }
        Cull Off
        ZWrite Off
        Lighting Off
        Blend [_SrcBlend] [_DstBlend]

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            float _ScrollSpeed;
            float _TwoLayer;
            float _Layer2Scale;
            float _Layer2Speed;
            float _Distort;
            float _AlphaFromLum;
            float _AlphaContrast;
            float _AlphaBoost;
            float _AlphaCutoff;
            float _AffineWarp;
            float _VertexSnap;
            float _SnapResolution;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv     : TEXCOORD0;
                fixed4 color  : COLOR;
            };

            struct v2f
            {
                float4 pos   : SV_POSITION;
                // Affine-safe UVs: xy = uv * persp, z = persp. Divide in the
                // fragment shader to undo the GPU's perspective correction.
                float3 uv1   : TEXCOORD0;
                float3 uv2   : TEXCOORD1;
                UNITY_FOG_COORDS(2)
                fixed4 color : TEXCOORD3;
            };

            // Quantise the vertex to a coarse screen grid, PS1-style.
            float4 SnapVertex(float4 clip, float grid)
            {
                float w = max(clip.w, 1e-5);
                clip.xy = round(clip.xy / w * grid) / grid * w;
                return clip;
            }

            v2f vert(appdata v)
            {
                v2f o;

                float4 clip = UnityObjectToClipPos(v.vertex);
                o.pos = lerp(clip, SnapVertex(clip, _SnapResolution), _VertexSnap);

                // Only the static tiling goes through the affine mapping. The
                // scroll is added per-fragment (below) so it never inherits the
                // affine/perspective distortion and stays perfectly smooth.
                float2 baseUV = v.uv * _MainTex_ST.xy;

                // Blend between perspective-correct (1) and fully affine (w).
                float persp = lerp(1.0, o.pos.w, _AffineWarp);
                o.uv1 = float3(baseUV * persp, persp);
                o.uv2 = float3(baseUV * _Layer2Scale * persp, persp);

                o.color = v.color * _Color;
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // Uniform scroll from the material Offset. Each layer is frac()'d on
                // its own so it stays precise forever and wraps seamlessly
                // (the texture must be set to Repeat).
                float2 drift = _MainTex_ST.zw * _ScrollSpeed * _Time.y;
                float2 vel1 = frac(drift);

                float2 uv1 = i.uv1.xy / i.uv1.z + vel1;
                fixed4 tex = tex2D(_MainTex, uv1);

                if (_TwoLayer > 0.5)
                {
                    float2 uv2 = i.uv2.xy / i.uv2.z + frac(drift * _Layer2Speed);
                    fixed4 b = tex2D(_MainTex, uv2);
                    // Second layer nudges the first around -> billowing, not sliding.
                    fixed4 a = tex2D(_MainTex, uv1 + (b.rg - 0.5) * _Distort);
                    tex = (a + b) * 0.5;
                }

                // Density. Fog textures usually have no alpha channel, so by
                // default drive it from the texture's brightness instead.
                float alpha = lerp(tex.a, Luminance(saturate(tex.rgb)), _AlphaFromLum);
                alpha = saturate(pow(saturate(alpha), _AlphaContrast) * _AlphaBoost);
                alpha *= i.color.a;                 // _Color.a * vertex-colour alpha

                clip(alpha - _AlphaCutoff);

                fixed4 col = fixed4(tex.rgb * i.color.rgb, alpha);
                UNITY_APPLY_FOG(i.fogCoord, col);   // tints rgb toward fog colour, leaves alpha
                return col;
            }
            ENDCG
        }
    }
    Fallback Off
}

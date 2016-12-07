Shader "WaveParticles/DefaultWaveSurface" {
    SubShader {
      Tags { "RenderType" = "Opaque" }
      CGPROGRAM
      #pragma surface surf Lambert vertex:vert
      struct Input {
          float2 uv_MainTex;
      };
      float _Amount;
      sampler2D _MainTex;
      void vert (inout appdata_full v) {
          v.vertex.y += tex2Dlod(_MainTex, float4(v.texcoord.xy, 0, 0)) * _Amount;
      }
      void surf (Input IN, inout SurfaceOutput o) {
          o.Albedo = float3(1.0f, 1.0f, 1.0f);
      }
      ENDCG
    } 
    Fallback "Diffuse"
  }
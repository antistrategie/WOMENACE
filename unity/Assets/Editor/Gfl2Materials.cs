using System.IO;
using UnityEditor;
using UnityEngine;

namespace WOMENACE.Editor
{
    // Materials for meshes ripped from the GFL2 client, which ship a texture set per material:
    // <set>_d albedo, _n tangent normal, _rmo (rough, metal, occlusion in the game's packing),
    // and _spc on hair. The callers create the assets, since each bake names and places its own.
    public static class Gfl2Materials
    {
        public const string SharedRamps = "Assets/Authored/shared/ramps";
        public const string SharedFaceSdf = "Assets/Authored/shared/face_sdf.png";

        public static Shader FindShader(string shaderName)
        {
            var shader = Shader.Find(shaderName);
            if (shader != null) return shader;
            Debug.LogError("missing shader " + shaderName + ", falling back to Standard");
            return Shader.Find("Standard");
        }

        // A texture set on the Doll shaders as shipped. The rmo is data in the game's own packing,
        // which the shader reads directly, so no HDRP repack or matte mask is involved, and the
        // Doll shaders sample no decal buffer, so they need no decal opt-out. faceSdf is set on a
        // face material only: the sweep's lookup coordinates exist on the face mesh alone.
        public static Material Doll(string shaderName, string name, string texDir, string texBase, string rampPath, string faceSdf = null)
        {
            var mat = new Material(FindShader(shaderName)) { name = name };
            var alb = BakeAssets.LoadTex(texDir, texBase, new[] { "_d", "_da" }, sRGB: true, isNormal: false);
            if (alb != null) mat.SetTexture("_BaseMap", alb);
            else Debug.LogWarning($"no albedo for {texBase} in {texDir}");
            var nrm = BakeAssets.LoadTex(texDir, texBase, new[] { "_n" }, sRGB: false, isNormal: true);
            if (nrm != null) mat.SetTexture("_NormalMap", nrm);
            var rmo = BakeAssets.LoadTex(texDir, texBase, new[] { "_rmo" }, sRGB: false, isNormal: false);
            if (rmo != null) mat.SetTexture("_MaskMap", rmo);
            var spc = BakeAssets.LoadTex(texDir, texBase, new[] { "_spc" }, sRGB: true, isNormal: false);
            if (spc != null)
            {
                mat.SetTexture("_SpecularMap", spc);
                // The PMX doll bakes keep this at 0 because a PMX source has no real TEXCOORD1 to
                // sample the streak with, but a ripped GFL2 mesh carries the game's own strip UV
                // (its hair's TEXCOORD1 spans strip coordinates well outside 0..1). A non-zero
                // intensity is also what routes the material off GGX onto the anisotropic hair term.
                mat.SetFloat("_MatCapIntensity", 1f);
            }
            var ramp = AssetDatabase.LoadAssetAtPath<Texture2D>(rampPath);
            if (ramp == null) Debug.LogError("missing ramp " + rampPath);
            else mat.SetTexture("_RampMap", ramp);
            if (faceSdf != null)
            {
                mat.SetFloat("_UseBlendTex", 1f);
                var sdf = AssetDatabase.LoadAssetAtPath<Texture2D>(faceSdf);
                if (sdf == null) Debug.LogError("missing face SDF map " + faceSdf);
                else mat.SetTexture("_SdfMap", sdf);
            }
            Debug.Log($"doll material {name}: shader={shaderName} ramp={Path.GetFileName(rampPath)} "
                + $"albedo={(alb ? alb.name : "-")} normal={(nrm ? nrm.name : "-")} rmo={(rmo ? rmo.name : "-")}");
            return mat;
        }

        // One layer of the eye stack (Womenace/DollEye, DollEyeShadow or DollEyeHighlight). Draw
        // order and blending live in the layer shaders, as on the dolls.
        public static Material Eye(string shaderName, string name, Texture2D baseMap)
        {
            var mat = new Material(FindShader(shaderName)) { name = name };
            if (baseMap != null) mat.SetTexture("_BaseMap", baseMap);
            else Debug.LogWarning($"no eye texture for {name}");
            return mat;
        }

        // A texture set on the Menace/character stub, which the game rebinds to its own character
        // shader by name. Every candidate property name is set so the rebind picks up whichever it
        // uses. smoothness, where given, also pins the metal and gloss values.
        public static Material Menace(Shader shader, string name, Texture2D albedo, Texture2D normal, Texture2D mask, float? smoothness = null)
        {
            var mat = new Material(shader) { name = name };
            if (albedo != null) foreach (var p in new[] { "_BaseMap", "_BaseColorMap", "_MainTex" }) if (mat.HasProperty(p)) mat.SetTexture(p, albedo);
            if (normal != null) foreach (var p in new[] { "_NormalMap", "_BumpMap", "_Normal" }) if (mat.HasProperty(p)) mat.SetTexture(p, normal);
            foreach (var p in new[] { "_MaskMap", "_Mask", "_MetallicGlossMap" }) if (mat.HasProperty(p)) mat.SetTexture(p, mask);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
            if (smoothness is float s)
            {
                if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0f);
                if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", s);
                if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", s);
            }
            // Native characters opt out of receiving decals, so road and other ground decals do not
            // project onto them. Their material carries _SupportDecals 0 and the _DISABLE_DECALS
            // keyword, so the serialised material sets both.
            if (mat.HasProperty("_SupportDecals")) mat.SetFloat("_SupportDecals", 0f);
            mat.EnableKeyword("_DISABLE_DECALS");
            return mat;
        }

        // A small linear mask in the HDRP layout (R metallic 0, G occlusion 1, B detail 0,
        // A smoothness). Without one the Menace/character shader reads a white mask, which is full
        // metal and full smoothness, so chrome.
        public static Texture2D MatteMask(string path, byte smoothness)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing != null) return existing;
            var t = new Texture2D(4, 4, TextureFormat.RGBA32, false, true);
            var px = new Color32[16];
            for (var i = 0; i < px.Length; i++) px[i] = new Color32(0, 255, 0, smoothness);
            t.SetPixels32(px);
            t.Apply();
            AssetDatabase.CreateAsset(t, path);
            return t;
        }
    }
}

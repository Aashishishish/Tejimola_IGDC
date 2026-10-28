#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Menu: Tejimola > VFX > Build Blood Flake Prefabs
/// Generates the placeholder "reddish-black flaky blood" VFX so nobody has to hand-build particle systems:
///   Assets/VFX/BloodFlakes/blood_flakes.png          4 irregular flake shapes in a 2x2 sheet
///   Assets/VFX/BloodFlakes/BloodFlake.mat            URP Particles/Unlit, alpha blended (black must stay visible in fog)
///   Assets/VFX/BloodFlakes/BloodFlakes_Hit.prefab    one-shot burst: many small flakes rise, a few big chunks fall
///   Assets/VFX/BloodFlakes/BloodFlakes_Evaporate.prefab  looping version for dissolving whole objects (shape set at runtime)
/// Re-running overwrites them. After that, tweak the prefabs by hand in the Inspector like any particle system.
/// </summary>
public static class BloodVfxBuilder
{
    private const string Folder = "Assets/VFX/BloodFlakes";

    // Palette: dried blood red -> almost black
    private static readonly Color Red = new Color(0.42f, 0.03f, 0.03f, 1f);
    private static readonly Color Black = new Color(0.06f, 0.01f, 0.01f, 1f);

    [MenuItem("Tejimola/VFX/Build Blood Flake Prefabs")]
    public static void Build()
    {
        Directory.CreateDirectory(Folder);
        var tex = BuildTexture();
        var mat = BuildMaterial(tex);
        BuildPrefab("BloodFlakes_Hit", mat, evaporate: false);
        BuildPrefab("BloodFlakes_Evaporate", mat, evaporate: true);
        AssetDatabase.SaveAssets();
        Debug.Log($"[BloodVfxBuilder] Built blood flake VFX in {Folder}");
    }

    // ---------------------------------------------------------------------------------------------
    // Texture: four jagged flakes (random-radius polygons) in a 2x2 sheet, soft 1px edge
    // ---------------------------------------------------------------------------------------------
    private static Texture2D BuildTexture()
    {
        const int cell = 64, size = cell * 2;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var px = new Color32[size * size];
        var rng = new System.Random(1234);

        for (int c = 0; c < 4; c++)
        {
            int ox = (c % 2) * cell, oy = (c / 2) * cell;
            int points = 6 + rng.Next(5);
            var radii = new float[points];
            // jaggedness: few spokes of very different lengths -> torn, chipped outline (not a petal, not a star)
            for (int i = 0; i < points; i++) radii[i] = 0.3f + (float)rng.NextDouble() * 0.7f;
            float squash = 0.55f + (float)rng.NextDouble() * 0.45f;                               // flakes are flat-ish

            for (int y = 0; y < cell; y++)
            for (int x = 0; x < cell; x++)
            {
                float u = (x + 0.5f) / cell * 2f - 1f, v = ((y + 0.5f) / cell * 2f - 1f) / squash;
                float ang = Mathf.Atan2(v, u) / (Mathf.PI * 2f) * points;
                if (ang < 0) ang += points;
                int i0 = (int)ang % points, i1 = (i0 + 1) % points;
                float edge = Mathf.Lerp(radii[i0], radii[i1], ang - Mathf.Floor(ang)) * 0.95f;
                float d = Mathf.Sqrt(u * u + v * v);
                float a = Mathf.Clamp01((edge - d) * cell * 0.5f);   // ~1px anti-aliased edge
                // white texture, colour comes from the particle; slight inner darkening gives the flake some body
                byte shade = (byte)(255 * Mathf.Lerp(0.75f, 1f, d / Mathf.Max(edge, 0.01f)));
                px[(oy + y) * size + ox + x] = new Color32(shade, shade, shade, (byte)(a * 255));
            }
        }

        tex.SetPixels32(px);
        tex.Apply();
        string path = Folder + "/blood_flakes.png";
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path);

        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    private static Material BuildMaterial(Texture2D tex)
    {
        string path = Folder + "/BloodFlake.mat";
        var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null) { mat = new Material(shader); AssetDatabase.CreateAsset(mat, path); }
        mat.shader = shader;
        mat.SetTexture("_BaseMap", tex);
        mat.SetColor("_BaseColor", Color.white);

        // Transparent, alpha blend (additive would make the black flakes invisible)
        mat.SetFloat("_Surface", 1f);
        mat.SetFloat("_Blend", 0f);
        mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        mat.SetFloat("_ZWrite", 0f);
        mat.SetFloat("_Cull", (float)CullMode.Off);
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.renderQueue = (int)RenderQueue.Transparent;
        EditorUtility.SetDirty(mat);
        return mat;
    }

    // ---------------------------------------------------------------------------------------------
    // Prefabs: root = rising flakes, child = falling chunks
    // ---------------------------------------------------------------------------------------------
    private static void BuildPrefab(string name, Material mat, bool evaporate)
    {
        var root = new GameObject(name);
        var flakes = root.AddComponent<ParticleSystem>();
        SetupFlakes(flakes, mat, evaporate);

        var chunkGo = new GameObject("Falling Chunks");
        chunkGo.transform.SetParent(root.transform, false);
        var chunks = chunkGo.AddComponent<ParticleSystem>();
        SetupChunks(chunks, mat, evaporate);

        PrefabUtility.SaveAsPrefabAsset(root, $"{Folder}/{name}.prefab");
        Object.DestroyImmediate(root);
    }

    private static void SetupFlakes(ParticleSystem ps, Material mat, bool evaporate)
    {
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.duration = evaporate ? 2f : 0.6f;
        main.loop = evaporate;
        main.playOnAwake = true;
        main.startLifetime = evaporate ? new ParticleSystem.MinMaxCurve(2f, 3.5f) : new ParticleSystem.MinMaxCurve(1.4f, 2.4f);
        main.startSpeed = evaporate ? new ParticleSystem.MinMaxCurve(0.2f, 0.8f) : new ParticleSystem.MinMaxCurve(0.6f, 1.8f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.16f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = new ParticleSystem.MinMaxGradient(Red, Black);
        // Negative gravity: flakes drift UP, slowly, so they hang around the hit long enough to read
        main.gravityModifier = evaporate ? -0.12f : -0.06f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = evaporate ? 600 : 80;
        main.stopAction = evaporate ? ParticleSystemStopAction.None : ParticleSystemStopAction.Destroy;

        var emission = ps.emission;
        if (evaporate)
        {
            emission.rateOverTime = 60f;   // EvaporateEffect scales this per object size
            emission.SetBursts(new ParticleSystem.Burst[0]);
        }
        else
        {
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 35, 50) });
        }

        var shape = ps.shape;
        shape.enabled = true;
        if (evaporate)
        {
            shape.shapeType = ParticleSystemShapeType.Box;   // EvaporateEffect sizes it to each renderer's bounds
            shape.scale = Vector3.one;
        }
        else
        {
            shape.shapeType = ParticleSystemShapeType.Cone;  // spray up and out from the hit
            shape.angle = 40f;
            shape.radius = 0.15f;
            shape.rotation = new Vector3(-90f, 0f, 0f);      // cone points along world up when the prefab is unrotated
        }

        // Flutter like ash: noise + spin
        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = 0.5f;
        noise.frequency = 0.8f;
        noise.scrollSpeed = 0.5f;

        var rot = ps.rotationOverLifetime;
        rot.enabled = true;
        rot.z = new ParticleSystem.MinMaxCurve(-3f, 3f);

        var drag = ps.limitVelocityOverLifetime;
        drag.enabled = true;
        drag.drag = evaporate ? 0.5f : 2.5f;   // bursts slow quickly, then drift

        var col = ps.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(0.55f, 0.45f, 0.45f), 1f) },   // darken as they dry
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.08f), new GradientAlphaKey(0.9f, 0.6f), new GradientAlphaKey(0f, 1f) });
        col.color = g;

        var sizeOverLife = ps.sizeOverLifetime;
        sizeOverLife.enabled = true;
        sizeOverLife.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.6f));

        SetupSheetAndRenderer(ps, mat);
    }

    private static void SetupChunks(ParticleSystem ps, Material mat, bool evaporate)
    {
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.duration = evaporate ? 2f : 0.6f;
        main.loop = evaporate;
        main.playOnAwake = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2f);
        main.startSpeed = evaporate ? new ParticleSystem.MinMaxCurve(0.2f, 1f) : new ParticleSystem.MinMaxCurve(1.5f, 3f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.14f, 0.3f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = new ParticleSystem.MinMaxGradient(Red * 0.8f, Black);
        main.gravityModifier = 1f;   // the few big pieces fall
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = evaporate ? 120 : 12;
        main.stopAction = ParticleSystemStopAction.None;   // the root destroys the whole prefab

        var emission = ps.emission;
        if (evaporate)
        {
            emission.rateOverTime = 6f;
            emission.SetBursts(new ParticleSystem.Burst[0]);
        }
        else
        {
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 5, 9) });
        }

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = evaporate ? ParticleSystemShapeType.Box : ParticleSystemShapeType.Cone;
        if (!evaporate)
        {
            shape.angle = 65f;
            shape.radius = 0.1f;
            shape.rotation = new Vector3(-90f, 0f, 0f);
        }

        var rot = ps.rotationOverLifetime;
        rot.enabled = true;
        rot.z = new ParticleSystem.MinMaxCurve(-6f, 6f);

        // Land and stop on the ground instead of falling through it
        var collision = ps.collision;
        collision.enabled = true;
        collision.type = ParticleSystemCollisionType.World;
        collision.quality = ParticleSystemCollisionQuality.High;   // few particles, so exact raycasts are cheap; Low let them fall through the terrain
        collision.dampen = 0.8f;
        collision.bounce = 0.1f;
        collision.collidesWith = ~(1 << 8);   // not the player
        collision.radiusScale = 0.3f;

        var col = ps.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.75f), new GradientAlphaKey(0f, 1f) });
        col.color = g;

        SetupSheetAndRenderer(ps, mat);
    }

    // Random flake shape per particle from the 2x2 sheet
    private static void SetupSheetAndRenderer(ParticleSystem ps, Material mat)
    {
        var sheet = ps.textureSheetAnimation;
        sheet.enabled = true;
        sheet.numTilesX = 2;
        sheet.numTilesY = 2;
        sheet.animation = ParticleSystemAnimationType.WholeSheet;
        sheet.frameOverTime = new ParticleSystem.MinMaxCurve(0f);
        sheet.startFrame = new ParticleSystem.MinMaxCurve(0f, 3.99f);
        sheet.cycleCount = 1;

        var r = ps.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = mat;
        r.renderMode = ParticleSystemRenderMode.Billboard;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
        r.sortMode = ParticleSystemSortMode.None;
    }
}
#endif

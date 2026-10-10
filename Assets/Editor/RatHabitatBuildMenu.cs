#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace RatHabitat.Editor
{
    public static class RatHabitatBuildMenu
    {
        private static readonly string[] Scenes = { "Assets/Scenes/Main.unity" };

        [MenuItem("Rat Empire/Build Android APK")]
        public static void BuildAndroid()
        {
            string path = EditorUtility.SaveFilePanel("Build Rat Empire Android APK", "", "RatEmpire-VerticalSlice.apk", "apk");
            if (string.IsNullOrEmpty(path)) return;
            EditorUserBuildSettings.buildAppBundle = false;
            BuildPlayerOptions options = new BuildPlayerOptions
            {
                scenes = Scenes,
                locationPathName = path,
                target = BuildTarget.Android,
                options = BuildOptions.None,
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            ShowResult(report, path);
        }

        [MenuItem("Rat Empire/Build Windows Test Player")]
        public static void BuildWindows()
        {
            string path = EditorUtility.SaveFilePanel("Build Rat Empire Windows Player", "", "RatEmpire-VerticalSlice.exe", "exe");
            if (string.IsNullOrEmpty(path)) return;
            BuildPlayerOptions options = new BuildPlayerOptions
            {
                scenes = Scenes,
                locationPathName = path,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            ShowResult(report, path);
        }

        private static void ShowResult(BuildReport report, string path)
        {
            if (report.summary.result == BuildResult.Succeeded)
            {
                EditorUtility.DisplayDialog("Rat Empire build complete", "Build created at:\n" + path + "\n\nSize: " + (report.summary.totalSize / (1024f * 1024f)).ToString("0.0") + " MB", "OK");
            }
            else
            {
                EditorUtility.DisplayDialog("Rat Empire build failed", report.summary.result + "\nCheck the Console for details.", "OK");
            }
        }
    }
}
namespace RatHabitat.Editor
{
    // Isolated preview scene only: never opens Main, runs GameBootstrap, or
    // touches PlayerPrefs/browser/colony saves. Images are generated once.
    public sealed class RatMarkingComparisonWindow : UnityEditor.EditorWindow
    {
        private UnityEngine.Texture2D[] images;
        private static readonly string[] Labels = {
            "Hooded • seed 101", "Hooded • seed 202", "Hooded • seed 303", "Hooded • seed 404",
            "Blaze • face seed 101", "Blaze • face seed 202", "Berkshire • legs", "Forced Hairless • Adult"
        };

        [UnityEditor.MenuItem("Rat Empire/Diagnostics/Markings and Hairless Comparison")]
        public static void Open()
        {
            var window=GetWindow<RatMarkingComparisonWindow>("Marking comparison");
            window.minSize=new UnityEngine.Vector2(1000,600);
            window.Show();
        }

        // Optional -executeMethod entry point; needs graphics, not -nographics.
        public static void CaptureBatch()
        {
            var images=Generate();
            try { Export(images); }
            finally { foreach (var image in images) UnityEngine.Object.DestroyImmediate(image); }
        }

        private void OnGUI()
        {
            UnityEditor.EditorGUILayout.HelpBox("Rest-pose hotspot comparison • no colony/save changes. " +
                "Top: same family, four seeds. Bottom: face, legs, adult skin.",UnityEditor.MessageType.Info);
            if (images==null || GUILayout.Button("Regenerate comparison"))
            {
                Release(); images=Generate();
            }
            float width=(position.width-24f)/4f;
            float height=(position.height-100f)/2f;
            for (int i=0;i<8;i++)
            {
                var cell=new Rect(8+(i%4)*width,55+(i/4)*height,width-6,height-6);
                GUI.Label(new Rect(cell.x,cell.y,cell.width,22),Labels[i],EditorStyles.boldLabel);
                GUI.DrawTexture(new Rect(cell.x,cell.y+24,cell.width,cell.height-24),images[i],ScaleMode.ScaleToFit);
            }
            if (GUI.Button(new Rect(8,position.height-30,240,24),"Export PNG to project Temp folder")) Export(images);
        }

        private void OnDisable() { Release(); }
        private void Release()
        {
            if (images==null) return;
            foreach (var image in images) if (image!=null) DestroyImmediate(image);
            images=null;
        }

        private static Texture2D[] Generate()
        {
            var images=new Texture2D[8];
            var preview=new PreviewRenderUtility();
            var root=new GameObject("Isolated marking comparison");
            preview.AddSingleGO(root);
            var factory=root.AddComponent<RatVisualFactory>();
            factory.handPaintedRatPrefab=AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Prefabs/HandPaintedRat/HandPaintedRat.prefab");
            try
            {
                for (int i=0;i<8;i++)
                {
                    string family=i<4 ? "Hooded" : i<6 ? "Blaze" : "Berkshire";
                    var genotype=GeneticsSystem.CreateFounder("B","B","C","C","D","D","S","S");
                    if (i==7) genotype.hairless=new LocusData("Hr","hr","hr");
                    string seed=i<4 ? ((i+1)*101).ToString() : i==5 ? "202" : "101";
                    var rat=ColonyFactory.CreateRat("hotspot-comparison-"+seed,Labels[i],RatSex.Female,
                        0L,0,genotype,new TraitData(50,80,70),RatStage.Adult);
                    rat.markingFamily=family; rat.coatColorVariant="black"; rat.coatTone=1f;
                    rat.phenotype=GeneticsSystem.DerivePhenotype(rat.stage,genotype,rat.coatColorVariant,1f);
                    GeneticsSystem.ApplyMarkingFamily(rat.phenotype,family);
                    var visual=factory.CreateStageVisual(root.transform,rat);
                    foreach (var animator in visual.GetComponentsInChildren<Animator>()) animator.enabled=false;
                    var renderer=visual.GetComponentInChildren<SkinnedMeshRenderer>();
                    if (renderer==null || ShaderUtil.ShaderHasError(renderer.sharedMaterials[0].shader))
                        throw new System.InvalidOperationException("Comparison model/shader failed validation.");
                    renderer.updateWhenOffscreen=true;
                    Bounds focus=renderer.bounds;
                    var rest=new System.Collections.Generic.List<Vector4>();
                    renderer.sharedMesh.GetUVs(2,rest);
                    if (i>=4 && i<7)
                    {
                        Vector3[] vertices=renderer.sharedMesh.vertices;
                        bool initialized=false;
                        for (int v=0;v<rest.Count;v++)
                        {
                            bool include=i<6 ? rest[v].z>.76f : rest[v].y<.34f && rest[v].z>.5f;
                            if (!include) continue;
                            Vector3 point=renderer.transform.TransformPoint(vertices[v]);
                            if (!initialized) { focus=new Bounds(point,Vector3.zero); initialized=true; }
                            else focus.Encapsulate(point);
                        }
                    }
                    preview.camera.orthographic=true;
                    preview.camera.orthographicSize=Mathf.Max(.01f,focus.extents.magnitude*.85f);
                    preview.camera.nearClipPlane=.001f; preview.camera.farClipPlane=100f;
                    // Model root faces -Z after the factory's authored rotation.
                    Vector3 angle=i==5 ? new Vector3(-1,.65f,-1) : new Vector3(1,.8f,-1);
                    preview.camera.transform.position=focus.center+angle.normalized*10f;
                    preview.camera.transform.LookAt(focus.center);
                    preview.camera.clearFlags=CameraClearFlags.SolidColor;
                    preview.camera.backgroundColor=new Color(.055f,.085f,.09f);
                    preview.lights[0].intensity=1.4f;
                    preview.lights[0].transform.rotation=Quaternion.Euler(35,-30,0);
                    preview.lights[1].intensity=.7f;
                    preview.BeginStaticPreview(new Rect(0,0,400,320));
                    preview.Render(true);
                    images[i]=preview.EndStaticPreview();
                    DestroyImmediate(visual);
                }
                return images;
            }
            catch
            {
                foreach (var image in images) if (image!=null) DestroyImmediate(image);
                throw;
            }
            finally { preview.Cleanup(); }
        }

        private static void Export(Texture2D[] images)
        {
            var sheet=new Texture2D(1600,640,TextureFormat.RGB24,false);
            try
            {
                for (int i=0;i<8;i++) sheet.SetPixels((i%4)*400,(1-i/4)*320,400,320,images[i].GetPixels());
                sheet.Apply();
                string folder=System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(),"Temp");
                System.IO.Directory.CreateDirectory(folder);
                string path=System.IO.Path.Combine(folder,"MarkingHotspotsComparison.png");
                System.IO.File.WriteAllBytes(path,sheet.EncodeToPNG());
                Debug.Log("[Rat Markings] Comparison saved: "+path+". Cells: "+string.Join("; ",Labels));
            }
            finally { DestroyImmediate(sheet); }
        }
    }
}
namespace RatHabitat.Editor
{
    // GPU regression check, not a C#-only check. A broken surface shader may
    // import successfully as an asset but lose every rat's phenotype color.
    public static class RatCoatShaderValidation
    {
        public static void ValidateBatch()
        {
            ValidateColors();
            Debug.Log("[Rat Coat Validation] PASS: shader passes compile; dark, blue, warm and albino coats render distinct colors.");
        }

        public static Color[] ValidateColors()
        {
            const string path = "Assets/Resources/HandPaintedRat/RatCoatSpotShader.shader";
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport |
                ImportAssetOptions.ForceUpdate);
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
            if (shader == null) throw new System.InvalidOperationException("Rat coat shader is missing.");
            var material = new Material(shader);
            var mesh = new Mesh();
            var preview = new PreviewRenderUtility();
            try
            {
                for (int pass = 0; pass < material.passCount; pass++)
                    ShaderUtil.CompilePass(material, pass, true);
                if (ShaderUtil.ShaderHasError(shader) || !shader.isSupported)
                {
                    string errors = "";
                    foreach (var message in ShaderUtil.GetShaderMessages(shader))
                        errors += "\n" + message.message + " (line " + message.line + ")";
                    throw new System.InvalidOperationException("Rat coat shader failed:" + errors);
                }

                mesh.vertices = new[] { new Vector3(-.5f,-.5f,0), new Vector3(.5f,-.5f,0),
                    new Vector3(.5f,.5f,0), new Vector3(-.5f,.5f,0) };
                mesh.triangles = new[] { 0,2,1,0,3,2 };
                mesh.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
                mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up };
                mesh.RecalculateBounds();
                var surface = new GameObject("Isolated coat color test");
                surface.AddComponent<MeshFilter>().sharedMesh = mesh;
                surface.AddComponent<MeshRenderer>().sharedMaterial = material;
                preview.AddSingleGO(surface);
                preview.camera.orthographic = true;
                preview.camera.orthographicSize = .6f;
                preview.camera.transform.position = new Vector3(0,0,-3);
                preview.camera.transform.rotation = Quaternion.identity;
                preview.camera.nearClipPlane = .1f;
                preview.camera.farClipPlane = 10f;
                preview.camera.clearFlags = CameraClearFlags.SolidColor;
                preview.camera.backgroundColor = Color.black;
                preview.lights[0].intensity = 1f;
                preview.lights[0].transform.rotation = Quaternion.identity;
                preview.lights[1].intensity = 0f;
                material.SetTexture("_FeatureSourceTex", Texture2D.whiteTexture);
                material.SetTexture("_FeatureMask", Texture2D.blackTexture);
                material.SetTexture("_EyeMask", Texture2D.blackTexture);
                material.SetFloat("_SpotStrength", 0f);
                material.SetFloat("_HairlessMode", 0f);
                var coats = new[] { new Color(.08f,.08f,.08f), new Color(.08f,.18f,.65f),
                    new Color(.65f,.24f,.08f), Color.white };
                var pixels = new Color[coats.Length];
                for (int i = 0; i < coats.Length; i++)
                {
                    material.SetColor("_Color", coats[i]);
                    material.SetColor("_AccentColor", coats[i]);
                    material.SetFloat("_AlbinoMode", i == 3 ? 1f : 0f);
                    preview.BeginStaticPreview(new Rect(0,0,64,64));
                    preview.Render(true);
                    var image = preview.EndStaticPreview();
                    try { pixels[i] = image.GetPixel(32,32); }
                    finally { Object.DestroyImmediate(image); }
                    Debug.Log("[Rat Coat Validation] coat " + i + " rendered " + pixels[i]);
                }
                if (pixels[3].grayscale < .2f || pixels[0].grayscale >= pixels[3].grayscale * .6f ||
                    pixels[1].b <= pixels[1].r * 1.6f || pixels[1].b <= pixels[1].g * 1.3f ||
                    pixels[2].r <= pixels[2].b * 1.4f)
                    throw new System.InvalidOperationException("Rendered coats lost their phenotype colors.");
                return pixels;
            }
            finally
            {
                preview.Cleanup();
                Object.DestroyImmediate(mesh);
                Object.DestroyImmediate(material);
            }
        }
    }
}
#endif

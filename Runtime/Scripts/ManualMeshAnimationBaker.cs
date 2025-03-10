namespace CodeWriter.MeshAnimation
{
#if UNITY_EDITOR
    using System;
    using System.Linq;
    using System.Collections.Generic;
    using JetBrains.Annotations;
    using UnityEditor;
    using UnityEditor.Presets;
    using UnityEngine;
    using Object = UnityEngine.Object;

    public static class ManualMeshAnimationBaker
    {
        private static readonly int AnimTextureProp = Shader.PropertyToID("_AnimTex");
        private static readonly int AnimationMulProp = Shader.PropertyToID("_AnimMul");
        private static readonly int AnimationAddProp = Shader.PropertyToID("_AnimAdd");
        private static readonly int AnimationNormalStartProp = Shader.PropertyToID("_AnimNormalStart");

        public static void Clear([NotNull] MeshAnimationAsset asset)
        {
            if (asset == null)
            {
                throw new ArgumentNullException(nameof(asset));
            }

            DestroyObject(ref asset.bakedMaterial);
            DestroyObject(ref asset.bakedTexture);

            foreach (var data in asset.extraMaterialData)
            {
                DestroyObject(ref data.material);
            }

            asset.extraMaterialData = new List<MeshAnimationAsset.ExtraMaterialData>();
            asset.animationData = new List<MeshAnimationAsset.AnimationData>();

            SaveAsset(asset);
        }

        private static void DestroyObject<T>(ref T field) where T : Object
        {
            if (field != null)
            {
                Object.DestroyImmediate(field, true);
                field = null;
            }
        }

        private static void CreateMaterial(MeshAnimationAsset asset)
        {
            var materialAssetName = asset.name + " Material";

            if (asset.bakedMaterial == null)
            {
                var material = new Material(asset.shader) { name = materialAssetName };
                asset.bakedMaterial = material;
                AssetDatabase.AddObjectToAsset(material, asset);
            }
            else
            {
                asset.bakedMaterial.shader = asset.shader;
                asset.bakedMaterial.name = materialAssetName;
            }

            if (asset.materialPreset != null)
            {
                var preset = new Preset(asset.materialPreset);
                if (preset.CanBeAppliedTo(asset.bakedMaterial))
                {
                    preset.ApplyTo(asset.bakedMaterial);
                }

                Object.DestroyImmediate(preset);
            }
        }

        private static void CreateExtraMaterials(MeshAnimationAsset asset)
        {
            foreach (var extra in asset.extraMaterials)
            {
                var data = asset.extraMaterialData.Find(it => it.name == extra.name);
                if (data == null)
                {
                    data = new MeshAnimationAsset.ExtraMaterialData
                    {
                        name = extra.name
                    };
                    asset.extraMaterialData.Add(data);
                }

                if (data.material == null)
                {
                    data.material = new Material(asset.shader) { name = $"{asset.name}_{extra.name} Material" };
                    AssetDatabase.AddObjectToAsset(data.material, asset);
                }

                data.material.shader = asset.shader;

                if (extra.preset != null)
                {
                    var preset = new Preset(extra.preset);
                    if (preset.CanBeAppliedTo(data.material))
                    {
                        preset.ApplyTo(data.material);
                    }

                    Object.DestroyImmediate(preset);
                }
            }

            foreach (var data in asset.extraMaterialData)
            {
                if (asset.extraMaterials.Any(extra => extra.name == data.name))
                {
                    continue;
                }

                Object.DestroyImmediate(data.material, true);

                data.material = null;
            }

            asset.extraMaterialData.RemoveAll(it => it.material == null);
        }

        private static void CreateTexture(MeshAnimationAsset asset, out bool aborted)
        {
            aborted = false;

            if (asset.bakedTexture == null)
            {
                var mesh = asset.skin.GetComponentInChildren<SkinnedMeshRenderer>().sharedMesh;
                var vertexCount = mesh.vertexCount;
                // extra height for normals
                var framesCount = asset.animationClips.Sum(clip => clip.GetFramesCount() + 1) * 2;

                if (vertexCount > 4096)
                {
                    var msg = $"WARNING: Mesh contains too many vertices ({vertexCount})";
                    if (!EditorUtility.DisplayDialog("Mesh Animator", msg, "Continue", "Abort"))
                    {
                        aborted = true;
                        return;
                    }
                }

                if (framesCount > 4096)
                {
                    var msg = $"WARNING: Mesh contains too many animation frames ({vertexCount})";
                    if (!EditorUtility.DisplayDialog("Mesh Animator", msg, "Continue", "Abort"))
                    {
                        aborted = true;
                        return;
                    }
                }

                var texWidth = asset.npotBakedTexture ? vertexCount : Mathf.NextPowerOfTwo(vertexCount);
                var textHeight = asset.npotBakedTexture ? framesCount : Mathf.NextPowerOfTwo(framesCount);
                var linear = asset.linearColorSpace;

                var texture = new Texture2D(texWidth, textHeight, TextureFormat.RGB24, false, linear)
                {
                    name = asset.name + " Texture",
                    hideFlags = HideFlags.NotEditable,
                    wrapMode = TextureWrapMode.Clamp,
                };

                AssetDatabase.AddObjectToAsset(texture, asset);
                asset.bakedTexture = texture;
            }
        }

        public static void PrepareBake(MeshAnimationAsset asset, BakeProgressInfo progressInfo)
        {
            if (asset == null)
            {
                throw new ArgumentNullException(nameof(asset));
            }

            var error = asset.GetValidationMessage();
            if (error != null)
            {
                throw new InvalidOperationException(error);
            }

            try
            {
                AssetDatabase.DisallowAutoRefresh();
                EditorUtility.DisplayProgressBar("Mesh Animator", "Baking", 0f);

                DestroyObject(ref asset.bakedTexture);
                CreateTexture(asset, out var aborted);
                if (!aborted)
                {
                    CreateMaterial(asset);
                    CreateExtraMaterials(asset);
                }

                var materials = new HashSet<Material> { asset.bakedMaterial };
                foreach (var data in asset.extraMaterialData)
                {
                    materials.Add(data.material);
                }

                foreach (var material in materials)
                {
                    material.SetTexture(AnimTextureProp, asset.bakedTexture);
                    material.SetVector(AnimationMulProp, progressInfo.boundMax - progressInfo.boundMin);
                    material.SetVector(AnimationAddProp, progressInfo.boundMin);
                    material.SetFloat(AnimationNormalStartProp, progressInfo.totalFrameCount);
                }


                SaveAsset(asset);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                AssetDatabase.AllowAutoRefresh();
            }
        }

        public class BakeProgressInfo
        {
            public Vector3 boundMin;
            public Vector3 boundMax;
            public int globalFrame;
            public int totalFrameCount;
            public int frame;
            public int framesCount;
            public bool looping;
        }


        public static void BakeSingleFrameAnimation(MeshAnimationAsset asset, BakeProgressInfo progressInfo, GameObject bakeObject)
        {
            var bakeMesh = new Mesh { hideFlags = HideFlags.HideAndDontSave };
            var skin = bakeObject.GetComponentInChildren<SkinnedMeshRenderer>();

            var bakeTransform = bakeObject.transform;
            bakeTransform.localPosition = Vector3.zero;
            bakeTransform.localRotation = Quaternion.identity;
            bakeTransform.localScale = Vector3.one;

            skin.BakeMesh(bakeMesh);

            CaptureMeshToTexture(asset.bakedTexture, bakeMesh, progressInfo.boundMin, progressInfo.boundMax, progressInfo.globalFrame, progressInfo.totalFrameCount);

            if (progressInfo.looping && progressInfo.frame == 0)
            {
                CaptureMeshToTexture(asset.bakedTexture, bakeMesh, progressInfo.boundMin, progressInfo.boundMax,
                    progressInfo.globalFrame + progressInfo.framesCount, progressInfo.totalFrameCount);
            }
            else if (!progressInfo.looping && progressInfo.frame == progressInfo.framesCount - 1)
            {
                CaptureMeshToTexture(asset.bakedTexture, bakeMesh, progressInfo.boundMin, progressInfo.boundMax, progressInfo.globalFrame + 1, progressInfo.totalFrameCount);
            }

            asset.bakedTexture.Apply();
            SaveAsset(asset);
        }

        private static void CaptureMeshToTexture(Texture2D texture, Mesh mesh, Vector3 min, Vector3 max, int line, int totalFrameCount)
        {
            var vertices = mesh.vertices;
            var normals = mesh.normals;

            for (var vertexId = 0; vertexId < vertices.Length; vertexId++)
            {
                // 存储位置
                var vertex = vertices[vertexId];
                var posColor = new Color(
                    Mathf.InverseLerp(min.x, max.x, vertex.x),
                    Mathf.InverseLerp(min.y, max.y, vertex.y),
                    Mathf.InverseLerp(min.z, max.z, vertex.z)
                );
                texture.SetPixel(vertexId, line, posColor);

                // 存储法线 (将[-1,1]范围映射到[0,1])
                var normal = normals[vertexId];
                var normalColor = new Color(
                    normal.x * 0.5f + 0.5f,
                    normal.y * 0.5f + 0.5f,
                    normal.z * 0.5f + 0.5f
                );
                texture.SetPixel(vertexId, line + totalFrameCount, normalColor);
            }
        }

        private static void SaveAsset(MeshAnimationAsset asset)
        {
            EditorUtility.SetDirty(asset);

            var assetPath = AssetDatabase.GetAssetPath(asset);
            AssetDatabase.ImportAsset(assetPath,
                ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        private static int GetFramesCount(this AnimationClip clip)
        {
            return Mathf.CeilToInt(clip.length * clip.frameRate);
        }

    }
#endif
}
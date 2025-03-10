namespace CodeWriter.MeshAnimation
{

    using System.Collections;
    using System.Collections.Generic;
    using UnityEngine;
    using TriInspector;
#if UNITY_EDITOR
    using UnityEditor;
#endif
    using System.Threading.Tasks;

    [ExecuteInEditMode]
    public class ManualMeshAnimationBakerPreview : MonoBehaviour
    {

        [SerializeField] private MeshAnimationAsset m_Asset;

        private static ManualMeshAnimationBaker.BakeProgressInfo _bakeProgressInfo;


#if UNITY_EDITOR
        private AnimationWindow _animationWindow;

        [Button]
        public void PrepareBake()
        {
            CreateBakeProgressInfo();
            ManualMeshAnimationBaker.PrepareBake(m_Asset, _bakeProgressInfo);
        }

        private void CreateBakeProgressInfo()
        {
            _animationWindow = EditorWindow.GetWindow<AnimationWindow>();
            _bakeProgressInfo = new ManualMeshAnimationBaker.BakeProgressInfo();
            _bakeProgressInfo.globalFrame = 0;
            _bakeProgressInfo.totalFrameCount = 0;
            var bakeMesh = new Mesh { hideFlags = HideFlags.HideAndDontSave };
            AnimationMode.StartAnimationMode();
            AnimationMode.BeginSampling();
            try
            {
                var skin = gameObject.GetComponentInChildren<SkinnedMeshRenderer>();
                m_Asset.animationData.Clear();
                foreach (var clip in m_Asset.animationClips)
                {
                    int framesCount = Mathf.CeilToInt(clip.length * clip.frameRate);
                    m_Asset.animationData.Add(new MeshAnimationAsset.AnimationData
                    {
                        name = clip.name,
                        startFrame = _bakeProgressInfo.totalFrameCount,
                        lengthFrames = framesCount,
                        lengthSeconds = clip.length,
                        looping = clip.isLooping,
                    });

                    for (int frame = 0; frame < framesCount; frame++)
                    {
                        EditorUtility.DisplayProgressBar("Mesh Animator", clip.name, 1f * frame / framesCount);

                        AnimationMode.SampleAnimationClip(gameObject, clip, frame / clip.frameRate);
                        skin.BakeMesh(bakeMesh);

                        var vertices = bakeMesh.vertices;
                        foreach (var vertex in vertices)
                        {
                            _bakeProgressInfo.boundMin = Vector3.Min(_bakeProgressInfo.boundMin, vertex);
                            _bakeProgressInfo.boundMax = Vector3.Max(_bakeProgressInfo.boundMax, vertex);
                        }
                    }
                    _bakeProgressInfo.totalFrameCount += (framesCount + 1);

                }
            }
            finally
            {
                AnimationMode.EndSampling();
                AnimationMode.StopAnimationMode();
                EditorUtility.ClearProgressBar();
                Object.DestroyImmediate(bakeMesh);
            }
        }

        [Button]
        public async void BakeCurrentFrame()
        {
            int clipIndex = 0;
            int currentFrame = 0;
            while (clipIndex < m_Asset.animationData.Count)
            {
                var animationData = m_Asset.animationData[clipIndex];
                _animationWindow.animationClip = m_Asset.animationClips[clipIndex];
                _animationWindow.frame = currentFrame;
                await BakeCurrentFrame(animationData, currentFrame);
                currentFrame++;

                if (currentFrame >= animationData.lengthFrames)
                {
                    currentFrame = 0;
                    ++clipIndex;
                }
            }
        }

        private async Task BakeCurrentFrame(MeshAnimationAsset.AnimationData animationData, int currentFrame)
        {
            _bakeProgressInfo.frame = currentFrame;
            _bakeProgressInfo.framesCount = (int) animationData.lengthFrames;
            _bakeProgressInfo.looping = animationData.looping;
            ManualMeshAnimationBaker.BakeSingleFrameAnimation(m_Asset, _bakeProgressInfo, gameObject);
            ++_bakeProgressInfo.globalFrame;

            if (_bakeProgressInfo.frame == _bakeProgressInfo.framesCount - 1)
            {
                ++_bakeProgressInfo.globalFrame;
            }

            await Task.Delay(100);
        }
#endif
    }
}

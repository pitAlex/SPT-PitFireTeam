using EFT;
using EFT.Visual;

using System;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.Rendering;

namespace pitTeam.Utils
{
    internal sealed class TeammatePingHighlight : IDisposable
    {
        private const string NativeMaterialName = "Hidden_HighLightMesh";
        private const string NativeShaderName = "Hidden/HighLightMesh";
        private const string InternalColorShaderName = "Hidden/Internal-Colored";
        private const float PingLineWidth = 1f;
        private const int NativeAlwaysPass = 3;
        private const int NativeCompositePass = 4;

        private static readonly int ColorProperty = Shader.PropertyToID("_Color");
        private static readonly int OffsetProperty = Shader.PropertyToID("_Offset");
        private static readonly int MaskTextureProperty = Shader.PropertyToID("_MaskRT");
        private static readonly int FinalTextureProperty = Shader.PropertyToID("_FinalRT");
        private static readonly int SourceBlendProperty = Shader.PropertyToID("_SrcBlend");
        private static readonly int DestinationBlendProperty = Shader.PropertyToID("_DstBlend");
        private static readonly int CullProperty = Shader.PropertyToID("_Cull");
        private static readonly int ZWriteProperty = Shader.PropertyToID("_ZWrite");
        private static readonly int ZTestProperty = Shader.PropertyToID("_ZTest");

        private readonly List<HighlightMeshTarget> _targets = new List<HighlightMeshTarget>();
        private readonly List<BotOwner> _teammates = new List<BotOwner>();
        private readonly List<Renderer> _rendererBuffer = new List<Renderer>();
        private readonly HashSet<Renderer> _seenRenderers = new HashSet<Renderer>();
        private readonly Dictionary<Renderer, HighlightMeshTarget> _targetCache = new Dictionary<Renderer, HighlightMeshTarget>();
        private readonly Dictionary<BotOwner, Material> _healthHighlightMaterials = new Dictionary<BotOwner, Material>();
        private readonly List<HighlightMeshTarget> _viewmodelTargets = new List<HighlightMeshTarget>();
        private readonly HashSet<Renderer> _seenViewmodelRenderers = new HashSet<Renderer>();

        private Camera _mainCamera;
        private CommandBuffer _commandBuffer;
        private Material _highlightMaterial;
        private Material _viewmodelMaskMaterial;
        private RenderTexture _maskTexture;
        private Player _localPlayer;
        private PlayerBody _cachedLocalPlayerBody;
        private Player.AbstractHandsController _cachedHandsController;
        private bool _viewmodelTargetsInitialized;
        private bool _loadFailureLogged;
        private bool _wasActive;

        public void Reset()
        {
            _commandBuffer?.Clear();
            ClearTargets();
            _wasActive = false;
        }

        public void Show(IReadOnlyList<BotData> teammates, Player localPlayer)
        {
            if (HasSameTeammateRoster(teammates, localPlayer))
            {
                return;
            }

            _commandBuffer?.Clear();
            ClearTargets();
            if (!EnsureInitialized())
            {
                return;
            }

            _localPlayer = localPlayer;
            for (int i = 0; i < teammates.Count; i++)
            {
                BotOwner bot = teammates[i]?.Data;
                Player player = bot?.GetPlayer;
                if (player == null || bot.IsDead || player.PlayerBody == null)
                {
                    continue;
                }

                _teammates.Add(bot);
            }

            RefreshTargets();
            RefreshViewmodelTargets();
        }

        private bool HasSameTeammateRoster(IReadOnlyList<BotData> teammates, Player localPlayer)
        {
            if (!ReferenceEquals(_localPlayer, localPlayer))
            {
                return false;
            }

            int currentIndex = 0;
            for (int i = 0; i < teammates.Count; i++)
            {
                BotOwner bot = teammates[i]?.Data;
                Player player = bot?.GetPlayer;
                if (player == null || bot.IsDead || player.PlayerBody == null)
                {
                    continue;
                }

                if (currentIndex >= _teammates.Count ||
                    !ReferenceEquals(_teammates[currentIndex], bot))
                {
                    return false;
                }

                currentIndex++;
            }

            return currentIndex == _teammates.Count;
        }

        private void RefreshTargets()
        {
            _targets.Clear();
            _seenRenderers.Clear();

            for (int teammateIndex = 0; teammateIndex < _teammates.Count; teammateIndex++)
            {
                BotOwner bot = _teammates[teammateIndex];
                Player player = bot?.GetPlayer;
                PlayerBody playerBody = player?.PlayerBody;
                if (player == null || bot.IsDead || playerBody == null)
                {
                    continue;
                }

                // EFT can swap visible renderers as a player enters view or changes LOD.
                // Re-read the authoritative body/equipment set throughout the ping so the
                // outline follows the live animated renderer instead of a stale one.
                _rendererBuffer.Clear();
                playerBody.GetRenderersNonAlloc(_rendererBuffer);
                for (int rendererIndex = 0; rendererIndex < _rendererBuffer.Count; rendererIndex++)
                {
                    Renderer renderer = _rendererBuffer[rendererIndex];
                    if (renderer == null ||
                        !renderer.enabled ||
                        !renderer.isVisible ||
                        !_seenRenderers.Add(renderer))
                    {
                        continue;
                    }

                    if (!_targetCache.TryGetValue(renderer, out HighlightMeshTarget target))
                    {
                        target = HighlightMeshTarget.TryCreate(renderer, bot);
                        if (target != null)
                        {
                            _targetCache[renderer] = target;
                        }
                    }

                    if (target != null)
                    {
                        _targets.Add(target);
                    }
                }
            }
        }

        public void Render(bool active)
        {
            if (_commandBuffer == null || _highlightMaterial == null)
            {
                return;
            }

            UpdateCameraAttachment();
            _commandBuffer.Clear();

            if (!active)
            {
                if (_wasActive || _targets.Count > 0)
                {
                    ClearTargets();
                }

                _wasActive = false;
                return;
            }

            _wasActive = true;
            RefreshTargets();
            RefreshViewmodelTargets();
            if (_mainCamera == null || _targets.Count == 0 || !EnsureMaskTexture())
            {
                return;
            }

            int pixelWidth = Mathf.Max(1, _mainCamera.pixelWidth);
            int pixelHeight = Mathf.Max(1, _mainCamera.pixelHeight);
            Vector4 outlineOffset = new Vector4(
                PingLineWidth / pixelWidth,
                PingLineWidth / pixelHeight,
                0f,
                0f);

            if (StatusReportHighlightColor.IsHealthColoringEnabled)
            {
                RenderHealthStatusHighlights(outlineOffset);
                return;
            }

            _highlightMaterial.SetColor(ColorProperty, StatusReportHighlightColor.GetConfiguredColor());
            _highlightMaterial.SetVector(OffsetProperty, outlineOffset);
            BeginMaskRender();
            for (int i = 0; i < _targets.Count; i++)
            {
                _targets[i].Draw(_commandBuffer, _highlightMaterial, NativeAlwaysPass);
            }

            DrawViewmodelMask();
            CompositeHighlight(_highlightMaterial);
        }

        private void RenderHealthStatusHighlights(Vector4 outlineOffset)
        {
            for (int teammateIndex = 0; teammateIndex < _teammates.Count; teammateIndex++)
            {
                BotOwner teammate = _teammates[teammateIndex];
                if (teammate == null || teammate.IsDead || !HasTargetsFor(teammate))
                {
                    continue;
                }

                Material material = GetHealthHighlightMaterial(teammate);
                material.SetColor(ColorProperty, StatusReportHighlightColor.GetConfiguredHealthColor(teammate));
                material.SetVector(OffsetProperty, outlineOffset);

                BeginMaskRender();
                for (int targetIndex = 0; targetIndex < _targets.Count; targetIndex++)
                {
                    HighlightMeshTarget target = _targets[targetIndex];
                    if (target.IsOwnedBy(teammate))
                    {
                        target.Draw(_commandBuffer, material, NativeAlwaysPass);
                    }
                }

                DrawViewmodelMask();
                CompositeHighlight(material);
            }
        }

        private bool HasTargetsFor(BotOwner teammate)
        {
            for (int i = 0; i < _targets.Count; i++)
            {
                if (_targets[i].IsOwnedBy(teammate))
                {
                    return true;
                }
            }

            return false;
        }

        private Material GetHealthHighlightMaterial(BotOwner teammate)
        {
            if (_healthHighlightMaterials.TryGetValue(teammate, out Material material) && material != null)
            {
                return material;
            }

            material = new Material(_highlightMaterial)
            {
                name = "pitFireTeam Status Report Health Highlight",
                hideFlags = HideFlags.HideAndDontSave
            };
            if (_maskTexture != null)
            {
                material.SetTexture(MaskTextureProperty, _maskTexture);
            }

            _healthHighlightMaterials[teammate] = material;
            return material;
        }

        private void BeginMaskRender()
        {
            _commandBuffer.SetRenderTarget(_maskTexture);
            _commandBuffer.ClearRenderTarget(false, true, Color.black);
        }

        private void DrawViewmodelMask()
        {
            if (_viewmodelMaskMaterial == null)
            {
                return;
            }

            for (int i = 0; i < _viewmodelTargets.Count; i++)
            {
                _viewmodelTargets[i].Draw(_commandBuffer, _viewmodelMaskMaterial, 0, requireVisible: true);
            }
        }

        private void CompositeHighlight(Material material)
        {
            _commandBuffer.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);
            _commandBuffer.GetTemporaryRT(FinalTextureProperty, -1, -1);
            _commandBuffer.Blit(
                BuiltinRenderTextureType.CameraTarget,
                FinalTextureProperty,
                material,
                NativeCompositePass);
            _commandBuffer.Blit(FinalTextureProperty, BuiltinRenderTextureType.CameraTarget);
            _commandBuffer.ReleaseTemporaryRT(FinalTextureProperty);
        }

        private void RefreshViewmodelTargets()
        {
            PlayerBody playerBody = _localPlayer?.PlayerBody;
            Player.AbstractHandsController handsController = _localPlayer?.HandsController;
            if (_viewmodelTargetsInitialized &&
                _cachedLocalPlayerBody == playerBody &&
                _cachedHandsController == handsController)
            {
                return;
            }

            _viewmodelTargetsInitialized = true;
            _cachedLocalPlayerBody = playerBody;
            _cachedHandsController = handsController;
            _viewmodelTargets.Clear();
            _seenViewmodelRenderers.Clear();

            if (playerBody?.BodySkins.TryGetValue(EBodyModelPart.Hands, out LoddedSkin handsSkin) == true)
            {
                foreach (Renderer renderer in handsSkin.GetRenderers())
                {
                    AddViewmodelTarget(renderer);
                }
            }

            GameObject controllerObject = handsController?.ControllerGameObject;
            if (controllerObject == null)
            {
                return;
            }

            Renderer[] controllerRenderers = controllerObject.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < controllerRenderers.Length; i++)
            {
                AddViewmodelTarget(controllerRenderers[i]);
            }
        }

        private void AddViewmodelTarget(Renderer renderer)
        {
            if (renderer == null || !_seenViewmodelRenderers.Add(renderer))
            {
                return;
            }

            HighlightMeshTarget target = HighlightMeshTarget.TryCreate(renderer, null);
            if (target != null)
            {
                _viewmodelTargets.Add(target);
            }
        }

        private bool EnsureInitialized()
        {
            if (_commandBuffer != null && _highlightMaterial != null)
            {
                UpdateCameraAttachment();
                return true;
            }

            Material sourceMaterial = FindNativeHighlightMaterial();
            if (sourceMaterial != null)
            {
                _highlightMaterial = new Material(sourceMaterial)
                {
                    name = "pitFireTeam Status Report Highlight",
                    hideFlags = HideFlags.HideAndDontSave
                };
            }
            else
            {
                Shader shader = Shader.Find(NativeShaderName);
                if (shader == null)
                {
                    LogLoadFailure($"EFT shader '{NativeShaderName}' was not loaded");
                    return false;
                }

                _highlightMaterial = new Material(shader)
                {
                    name = "pitFireTeam Status Report Highlight",
                    hideFlags = HideFlags.HideAndDontSave
                };
            }

            _highlightMaterial.SetColor(ColorProperty, StatusReportHighlightColor.GetConfiguredColor());
            _viewmodelMaskMaterial = CreateViewmodelMaskMaterial();
            _commandBuffer = new CommandBuffer { name = "pitFireTeam Status Report Teammates" };
            _loadFailureLogged = false;
            UpdateCameraAttachment();
            return true;
        }

        private static Material FindNativeHighlightMaterial()
        {
            Material[] materials = Resources.FindObjectsOfTypeAll<Material>();
            for (int i = 0; i < materials.Length; i++)
            {
                Material material = materials[i];
                if (material != null &&
                    string.Equals(material.name, NativeMaterialName, StringComparison.Ordinal) &&
                    material.shader != null &&
                    string.Equals(material.shader.name, NativeShaderName, StringComparison.Ordinal))
                {
                    return material;
                }
            }

            return null;
        }

        private static Material CreateViewmodelMaskMaterial()
        {
            Shader shader = Shader.Find(InternalColorShaderName);
            if (shader == null)
            {
                pitFireTeam.Log.LogWarning(
                    $"Status Report viewmodel masking unavailable: Unity shader '{InternalColorShaderName}' was not loaded.");
                return null;
            }

            Material material = new Material(shader)
            {
                name = "pitFireTeam Status Report Viewmodel Mask",
                hideFlags = HideFlags.HideAndDontSave
            };
            material.SetColor(ColorProperty, Color.black);
            material.SetInt(SourceBlendProperty, (int)BlendMode.One);
            material.SetInt(DestinationBlendProperty, (int)BlendMode.Zero);
            material.SetInt(CullProperty, (int)CullMode.Off);
            material.SetInt(ZWriteProperty, 0);
            material.SetInt(ZTestProperty, (int)CompareFunction.Always);
            return material;
        }

        private bool EnsureMaskTexture()
        {
            int width = Mathf.Max(1, Screen.width);
            int height = Mathf.Max(1, Screen.height);
            if (_maskTexture != null && _maskTexture.width == width && _maskTexture.height == height)
            {
                return true;
            }

            DestroyMaskTexture();
            _maskTexture = new RenderTexture(width, height, 0, RenderTextureFormat.R8)
            {
                name = "pitFireTeam Status Report Mask",
                hideFlags = HideFlags.HideAndDontSave
            };
            _maskTexture.Create();
            if (!_maskTexture.IsCreated())
            {
                LogLoadFailure("the teammate mask render texture could not be created");
                DestroyMaskTexture();
                return false;
            }

            _highlightMaterial.SetTexture(MaskTextureProperty, _maskTexture);
            foreach (Material material in _healthHighlightMaterials.Values)
            {
                if (material != null)
                {
                    material.SetTexture(MaskTextureProperty, _maskTexture);
                }
            }

            return true;
        }

        private void UpdateCameraAttachment()
        {
            if (_commandBuffer == null)
            {
                return;
            }

            Camera nextCamera = EFT.CameraControl.CameraManager.Instance?.Camera ?? Camera.main;
            if (_mainCamera == nextCamera)
            {
                return;
            }

            DetachCamera();
            _mainCamera = nextCamera;
            if (_mainCamera != null)
            {
                _mainCamera.RemoveCommandBuffer(CameraEvent.AfterImageEffectsOpaque, _commandBuffer);
                _mainCamera.AddCommandBuffer(CameraEvent.AfterImageEffectsOpaque, _commandBuffer);
            }
        }

        private void DetachCamera()
        {
            if (_mainCamera != null && _commandBuffer != null)
            {
                _mainCamera.RemoveCommandBuffer(CameraEvent.AfterImageEffectsOpaque, _commandBuffer);
            }

            _mainCamera = null;
        }

        private void ClearTargets()
        {
            _targets.Clear();
            _teammates.Clear();
            _rendererBuffer.Clear();
            _seenRenderers.Clear();
            _targetCache.Clear();
            foreach (Material material in _healthHighlightMaterials.Values)
            {
                if (material != null)
                {
                    UnityEngine.Object.Destroy(material);
                }
            }

            _healthHighlightMaterials.Clear();
            _viewmodelTargets.Clear();
            _seenViewmodelRenderers.Clear();
            _localPlayer = null;
            _cachedLocalPlayerBody = null;
            _cachedHandsController = null;
            _viewmodelTargetsInitialized = false;
        }

        private void DestroyMaskTexture()
        {
            if (_maskTexture == null)
            {
                return;
            }

            _maskTexture.Release();
            UnityEngine.Object.Destroy(_maskTexture);
            _maskTexture = null;
        }

        private void LogLoadFailure(string reason)
        {
            if (_loadFailureLogged)
            {
                return;
            }

            _loadFailureLogged = true;
            pitFireTeam.Log.LogWarning($"Status Report teammate outline unavailable: {reason}.");
        }

        public void Dispose()
        {
            ClearTargets();
            DetachCamera();

            _commandBuffer?.Release();
            _commandBuffer = null;

            DestroyMaskTexture();

            if (_highlightMaterial != null)
            {
                UnityEngine.Object.Destroy(_highlightMaterial);
                _highlightMaterial = null;
            }

            if (_viewmodelMaskMaterial != null)
            {
                UnityEngine.Object.Destroy(_viewmodelMaskMaterial);
                _viewmodelMaskMaterial = null;
            }

            _wasActive = false;
        }

        private sealed class HighlightMeshTarget
        {
            private readonly Renderer _renderer;
            private readonly BotOwner _owner;

            private HighlightMeshTarget(Renderer renderer, BotOwner owner)
            {
                _renderer = renderer;
                _owner = owner;
            }

            public static HighlightMeshTarget TryCreate(Renderer renderer, BotOwner owner)
            {
                if (renderer is SkinnedMeshRenderer skinnedRenderer)
                {
                    Mesh sharedMesh = skinnedRenderer.sharedMesh;
                    return sharedMesh != null
                        ? new HighlightMeshTarget(renderer, owner)
                        : null;
                }

                if (renderer is MeshRenderer)
                {
                    MeshFilter meshFilter = renderer.GetComponent<MeshFilter>();
                    if (meshFilter?.sharedMesh != null)
                    {
                        return new HighlightMeshTarget(renderer, owner);
                    }
                }

                return null;
            }

            public bool IsOwnedBy(BotOwner owner)
            {
                return ReferenceEquals(_owner, owner);
            }

            public void Draw(
                CommandBuffer commandBuffer,
                Material material,
                int pass,
                bool requireVisible = false)
            {
                if (_renderer == null ||
                    !_renderer.enabled ||
                    (requireVisible &&
                        (_renderer.forceRenderingOff ||
                         !_renderer.gameObject.activeInHierarchy ||
                         !_renderer.isVisible)))
                {
                    return;
                }

                Mesh mesh = (_renderer as SkinnedMeshRenderer)?.sharedMesh ??
                    (_renderer as MeshRenderer)?.GetComponent<MeshFilter>()?.sharedMesh;
                if (mesh == null)
                {
                    return;
                }

                for (int subMeshIndex = 0; subMeshIndex < mesh.subMeshCount; subMeshIndex++)
                {
                    commandBuffer.DrawRenderer(_renderer, material, subMeshIndex, pass);
                }
            }
        }
    }
}

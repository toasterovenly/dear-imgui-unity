﻿using UnityEngine;
using UnityEngine.Rendering;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Profiling;
using System.Collections.Generic;

namespace ImGuiNET.Unity
{
    using ImGuiUtil;

    /// <summary>
    /// Renderer bindings in charge of producing instructions for rendering ImGui draw data.
    /// Uses DrawMesh.
    /// </summary>
    sealed class ImGuiRendererMesh : IImGuiRenderer
    {
        readonly Shader _shader;
        readonly int _texID;

        Material _material;
        readonly MaterialPropertyBlock _properties = new MaterialPropertyBlock();

        readonly TextureManager _texManager;

        Mesh _mesh;
        // Color sent with TexCoord1 semantics because otherwise Color attribute would be reordered to come before UVs
        static readonly VertexAttributeDescriptor[] s_attributes = new[]
        {   // ImDrawVert layout
            new VertexAttributeDescriptor(VertexAttribute.Position , VertexAttributeFormat.Float32, 2), // position
            new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2), // uv
            new VertexAttributeDescriptor(VertexAttribute.TexCoord1, VertexAttributeFormat.UInt32 , 1), // color
        };
        // skip all checks and validation when updating the mesh
        const MeshUpdateFlags NoMeshChecks = MeshUpdateFlags.DontNotifyMeshUsers | MeshUpdateFlags.DontRecalculateBounds
                                           | MeshUpdateFlags.DontResetBoneBounds | MeshUpdateFlags.DontValidateIndices;
        int _prevSubMeshCount = 1;  // number of sub meshes used previously

        List<SubMeshDescriptor> descriptors = new List<SubMeshDescriptor>();
        static readonly ProfilerMarker s_updateMeshPerfMarker = new ProfilerMarker("DearImGui.RendererMesh.UpdateMesh");
        static readonly ProfilerMarker s_createDrawComandsPerfMarker = new ProfilerMarker("DearImGui.RendererMesh.CreateDrawCommands");

        public ImGuiRendererMesh(ShaderResourcesAsset resources, TextureManager texManager)
        {
            _shader = resources.shaders.mesh;
            _texManager = texManager;
            _texID = Shader.PropertyToID(resources.propertyNames.tex);
        }

        public void Initialize(ImGuiIOPtr io)
        {
            io.SetBackendRendererName("Unity Mesh");                            // setup renderer info and capabilities
            io.BackendFlags |= ImGuiBackendFlags.RendererHasVtxOffset;          // supports ImDrawCmd::VtxOffset to output large meshes while still using 16-bits indices

            _material = new Material(_shader) { hideFlags = HideFlags.HideAndDontSave & ~HideFlags.DontUnloadUnusedAsset };
            _mesh = new Mesh() { name = "DearImGui Mesh" };
            _mesh.MarkDynamic();
        }

        public void Shutdown(ImGuiIOPtr io)
        {
            io.SetBackendRendererName(null);

            if (_mesh     != null) { Object.Destroy(_mesh);      _mesh     = null; }
            if (_material != null) { Object.Destroy(_material);  _material = null; }
        }

        public void RenderDrawLists(CommandBuffer cmd, ImDrawDataPtr drawData)
        {
            float renderScale = 1.0f;
            if (RenderUtils.IsUsingURP())
            {
                var qualityLevel = QualitySettings.GetQualityLevel();
                var urp = (UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset)QualitySettings.GetRenderPipelineAssetAt(qualityLevel);
                renderScale = urp.renderScale;
            }

            Vector2 fbSize = (drawData.DisplaySize * drawData.FramebufferScale * renderScale).ToUnityVector();
            if (fbSize.x <= 0f || fbSize.y <= 0f || drawData.TotalVtxCount == 0)
                return; // avoid rendering when minimized

            s_updateMeshPerfMarker.Begin();
            UpdateMesh(drawData, fbSize);
            s_updateMeshPerfMarker.End();

            cmd.BeginSample("DearImGui.ExecuteDrawCommands");
            s_createDrawComandsPerfMarker.Begin();
            CreateDrawCommands(cmd, drawData, fbSize);
            s_createDrawComandsPerfMarker.End();
            cmd.EndSample("DearImGui.ExecuteDrawCommands");
        }

        unsafe void UpdateMesh(ImDrawDataPtr drawData, Vector2 fbSize)
        {
            int subMeshCount = 0; // nr of submeshes is the same as the nr of ImDrawCmd
            for (int n = 0, nMax = drawData.CmdListsCount; n < nMax; ++n)
                subMeshCount += drawData.CmdListsRange[n].CmdBuffer.Size;

            // set mesh structure
            if (_prevSubMeshCount != subMeshCount)
            {
                _mesh.Clear(true); // occasionally crashes when changing subMeshCount without clearing first
                _mesh.subMeshCount = _prevSubMeshCount = subMeshCount;
            }
            _mesh.SetVertexBufferParams(drawData.TotalVtxCount, s_attributes);
            _mesh.SetIndexBufferParams (drawData.TotalIdxCount, IndexFormat.UInt16);

            // upload data into mesh
            int vtxOf = 0;
            int idxOf = 0;
            descriptors.Clear();
            for (int n = 0, nMax = drawData.CmdListsCount; n < nMax; ++n)
            {
                ImDrawListPtr drawList = drawData.CmdListsRange[n];
                NativeArray<ImDrawVert> vtxArray = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<ImDrawVert>(
                    (void*)drawList.VtxBuffer.Data, drawList.VtxBuffer.Size, Allocator.None);
                NativeArray<ushort>     idxArray = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<ushort>(
                    (void*)drawList.IdxBuffer.Data, drawList.IdxBuffer.Size, Allocator.None);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
                NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref vtxArray, AtomicSafetyHandle.GetTempMemoryHandle());
                NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref idxArray, AtomicSafetyHandle.GetTempMemoryHandle());
#endif
                // upload vertex/index data
                _mesh.SetVertexBufferData(vtxArray, 0, vtxOf, vtxArray.Length, 0, NoMeshChecks);
                _mesh.SetIndexBufferData (idxArray, 0, idxOf, idxArray.Length,    NoMeshChecks);

                // define subMeshes
                for (int i = 0, iMax = drawList.CmdBuffer.Size; i < iMax; ++i)
                {
                    ImDrawCmdPtr cmd = drawList.CmdBuffer[i];
                    var descriptor = new SubMeshDescriptor
                    {
                        topology = MeshTopology.Triangles,
                        indexStart = idxOf + (int)cmd.IdxOffset,
                        indexCount = (int)cmd.ElemCount,
                        baseVertex = vtxOf + (int)cmd.VtxOffset,
                    };
                    descriptors.Add(descriptor);
                }
                vtxOf += vtxArray.Length;
                idxOf += idxArray.Length;
            }
            _mesh.SetSubMeshes(descriptors, NoMeshChecks);
            _mesh.UploadMeshData(false);
        }

        void CreateDrawCommands(CommandBuffer cmd, ImDrawDataPtr drawData, Vector2 fbSize)
        {
            float renderScale = 1.0f;
            if (RenderUtils.IsUsingURP())
            {
                var qualityLevel = QualitySettings.GetQualityLevel();
                var urp = (UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset)QualitySettings.GetRenderPipelineAssetAt(qualityLevel);
                renderScale = urp.renderScale;
            }

            var prevTextureId = System.IntPtr.Zero;
            var clipOffst = new Vector4(drawData.DisplayPos.X, drawData.DisplayPos.Y, drawData.DisplayPos.X, drawData.DisplayPos.Y);
            var clipScale = new Vector4(drawData.FramebufferScale.X, drawData.FramebufferScale.Y, drawData.FramebufferScale.X, drawData.FramebufferScale.Y) * renderScale;

            var TRS = Matrix4x4.TRS(
                new Vector3(0.5f / fbSize.x, 0.5f / fbSize.y, 0f), // small adjustment to improve text
                Quaternion.identity,
                new Vector3(renderScale, renderScale, 0f));
            cmd.SetViewport(new Rect(0f, 0f, fbSize.x, fbSize.y));
            cmd.SetViewProjectionMatrices(
                TRS,
                Matrix4x4.Ortho(0f, fbSize.x, fbSize.y, 0f, 0f, 1f));

            int subOf = 0;
            for (int n = 0, nMax = drawData.CmdListsCount; n < nMax; ++n)
            {
                ImDrawListPtr drawList = drawData.CmdListsRange[n];
                for (int i = 0, iMax = drawList.CmdBuffer.Size; i < iMax; ++i, ++subOf)
                {
                    ImDrawCmdPtr drawCmd = drawList.CmdBuffer[i];
                    // TODO: user callback in drawCmd.UserCallback & drawCmd.UserCallbackData

                    // project scissor rectangle into framebuffer space and skip if fully outside
                    var clip = Vector4.Scale(new Vector4(drawCmd.ClipRect.X, drawCmd.ClipRect.Y, drawCmd.ClipRect.Z, drawCmd.ClipRect.W) - clipOffst, clipScale);
                    if (clip.x >= fbSize.x || clip.y >= fbSize.y || clip.z < 0f || clip.w < 0f) continue;

                    if (prevTextureId != drawCmd.TextureId)
                        _properties.SetTexture(_texID, _texManager.GetTexture((int)(prevTextureId = drawCmd.TextureId)));

                    cmd.EnableScissorRect(new Rect(clip.x, fbSize.y - clip.w, clip.z - clip.x, clip.w - clip.y)); // invert y
                    cmd.DrawMesh(_mesh, Matrix4x4.identity, _material, subOf, -1, _properties);
                }
            }
            cmd.DisableScissorRect();
        }
    }
}

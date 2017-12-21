﻿using GrassSimulation.Core.Lod;
using UnityEngine;
using UnityEngine.Rendering;

namespace GrassSimulation.Core.Billboard
{
	public class BillboardTexturePatch : Patch
	{
		private readonly uint[] _argsGeometry = {0, 0, 0, 0, 0};
		private readonly ComputeBuffer _argsGeometryBuffer;
		private readonly MaterialPropertyBlock _materialPropertyBlock;
		private readonly float _parameterOffsetX;
		private readonly float _parameterOffsetY;
		private readonly Matrix4x4 _patchModelMatrix;
		private readonly Vector4 _patchTexCoord; //x: xStart, y: yStart, z: width, w:height
		private readonly int _startIndex;
		private Mesh _dummyMesh;
		private Texture2D _normalHeightTexture;
		private RenderTexture _simulationTexture;
		private Texture2D _boundsTexture1;
		private Texture2D _boundsTexture2;
		

		public BillboardTexturePatch(SimulationContext ctx) : base(ctx)
		{
			_patchTexCoord = new Vector4(0, 0, 1, 1);
			Bounds = new Bounds(Vector3.zero, Vector3.one);
			_startIndex = Ctx.Random.Next(0,
				(int) (Ctx.Settings.GetSharedBufferLength() - Ctx.Settings.GetMaxAmountBladesPerPatch()));
			_materialPropertyBlock = new MaterialPropertyBlock();
			_parameterOffsetX = (float) Ctx.Random.NextDouble();
			_parameterOffsetY = (float) Ctx.Random.NextDouble();
			_patchModelMatrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity,
				new Vector3(0.5f, 1f, 0.5f));

			_argsGeometryBuffer =
				new ComputeBuffer(1, _argsGeometry.Length * sizeof(uint), ComputeBufferType.IndirectArguments);
			_argsGeometry[0] = Ctx.Settings.BillboardTextureGrassCount; //Vertex Count
			_argsGeometry[1] = 1;
			_argsGeometryBuffer.SetData(_argsGeometry);

			CreateGrassDataTexture();
			CreateDummyMesh();
			SetupMaterialPropertyBlock();
		}

		public override bool IsLeaf
		{
			get { return true; }
		}

		public void Destroy()
		{
			//TODO: Clean up buffers and textures
			_argsGeometryBuffer.Release();
		}

		public void Draw()
		{
			//RunSimulationComputeShader();
			if (_argsGeometry[1] > 0)
				Graphics.DrawMeshInstancedIndirect(_dummyMesh, 0, Ctx.GrassBillboardGeneration, Bounds, _argsGeometryBuffer, 0,
					_materialPropertyBlock, ShadowCastingMode.Off, false, 0, Ctx.BillboardTextureCamera);
		}

		private void SetupMaterialPropertyBlock()
		{
			//TODO: Add option to update things like matrix not only on startup but also on update
			_materialPropertyBlock.SetFloat("StartIndex", _startIndex);
			_materialPropertyBlock.SetFloat("ParameterOffsetX", _parameterOffsetX);
			_materialPropertyBlock.SetFloat("ParameterOffsetY", _parameterOffsetY);
			_materialPropertyBlock.SetVector("PatchTexCoord", _patchTexCoord);
			_materialPropertyBlock.SetTexture("SimulationTexture", _simulationTexture);
			_materialPropertyBlock.SetTexture("NormalHeightTexture", _normalHeightTexture);
			_materialPropertyBlock.SetMatrix("PatchModelMatrix", _patchModelMatrix);
		}

		
		
		public Bounds GetBillboardBounding()
		{
			Vector3 min = Vector3.positiveInfinity;
			Vector3 max = Vector3.negativeInfinity;

			for (int i = _startIndex; i < _startIndex + Ctx.Settings.BillboardTextureGrassCount; i++)
			{
				var localV0 = new Vector3(Ctx.GrassInstance.UvData[i].Position.x, 0, Ctx.GrassInstance.UvData[i].Position.y);
				var v0 = _patchModelMatrix.MultiplyPoint3x4(localV0);
				var v1Sample = _boundsTexture1.GetPixelBilinear(localV0.x, localV0.z);
				var v2Sample = _boundsTexture2.GetPixelBilinear(localV0.x, localV0.z);
				var v1 = v0 + new Vector3(v1Sample.r, v1Sample.g, v1Sample.b);
				var v2 = v0 + new Vector3(v2Sample.r, v2Sample.g, v2Sample.b);
				min = Vector3.Min(min, Vector3.Min(Vector3.Min(v0, v1), v2));
				max = Vector3.Max(max, Vector3.Max(Vector3.Max(v0, v1), v2));
			}
			
			var retBounds = new Bounds();
			retBounds.SetMinMax(min, max);
			
			Bounds = retBounds;
			return retBounds;
		}
		
		public void RunSimulationComputeShader()
		{
			//Set per patch data for whole compute shader
			Ctx.GrassSimulationComputeShader.SetInt("StartIndex", _startIndex);
			Ctx.GrassSimulationComputeShader.SetFloat("ParameterOffsetX", _parameterOffsetX);
			Ctx.GrassSimulationComputeShader.SetVector("PatchTexCoord", _patchTexCoord);
			Ctx.GrassSimulationComputeShader.SetFloat("ParameterOffsetY", _parameterOffsetY);
			Ctx.GrassSimulationComputeShader.SetFloat("GrassDataResolution", Ctx.Settings.GrassDataResolution);
			Ctx.GrassSimulationComputeShader.SetMatrix("PatchModelMatrix", _patchModelMatrix);

			//Set buffers for Physics Kernel
			Ctx.GrassSimulationComputeShader.SetTexture(Ctx.KernelPhysics, "SimulationTexture", _simulationTexture);
			Ctx.GrassSimulationComputeShader.SetTexture(Ctx.KernelPhysics, "NormalHeightTexture", _normalHeightTexture);

			uint threadGroupX, threadGroupY, threadGroupZ;
			Ctx.GrassSimulationComputeShader.GetKernelThreadGroupSizes(Ctx.KernelPhysics, out threadGroupX, out threadGroupY,
				out threadGroupZ);

			//Run Physics Simulation
			Ctx.GrassSimulationComputeShader.Dispatch(Ctx.KernelPhysics, (int) (Ctx.Settings.GrassDataResolution / threadGroupX),
				(int) (Ctx.Settings.GrassDataResolution / threadGroupY), 1);
			
			_boundsTexture1 = Utils.RenderTextureUtils.GetRenderTextureVolumeElementAsTexture2D(Ctx.RenderTextureVolumeToSlice, _simulationTexture, 0,
				TextureFormat.RGBAFloat, false, true);
			_boundsTexture2 = Utils.RenderTextureUtils.GetRenderTextureVolumeElementAsTexture2D(Ctx.RenderTextureVolumeToSlice, _simulationTexture, 1,
				TextureFormat.RGBAFloat, false, true);
		}

		private void CreateGrassDataTexture()
		{
			_normalHeightTexture = new Texture2D(Ctx.Settings.GetPerPatchTextureWidthHeight(),
				Ctx.Settings.GetPerPatchTextureWidthHeight(),
				TextureFormat.RGBAFloat, false, true)
			{
				filterMode = FilterMode.Bilinear,
				wrapMode = TextureWrapMode.Clamp
			};
			var textureData = new Color[Ctx.Settings.GetPerPatchTextureLength()];
			var i = 0;
			for (var y = 0; y < Ctx.Settings.GetPerPatchTextureWidthHeight(); y++)
			for (var x = 0; x < Ctx.Settings.GetPerPatchTextureWidthHeight(); x++)
			{
				var posY = 0f;
				var up = Vector3.up;

				textureData[i] = new Color(up.x, up.y, up.z, posY);
				i++;
			}

			_normalHeightTexture.SetPixels(textureData);
			_normalHeightTexture.Apply();

			_simulationTexture = new RenderTexture(Ctx.Settings.GetPerPatchTextureWidthHeight(),
				Ctx.Settings.GetPerPatchTextureWidthHeight(), 0,
				RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear)
			{
				//TODO: Remove if mipmaps not used or use mipmaps
				filterMode = Ctx.Settings.GrassDataTrilinearFiltering ? FilterMode.Trilinear : FilterMode.Bilinear,
				autoGenerateMips = Ctx.Settings.GrassDataTrilinearFiltering,
				useMipMap = Ctx.Settings.GrassDataTrilinearFiltering,
				dimension = TextureDimension.Tex2DArray,
				volumeDepth = 2,
				enableRandomWrite = true,
				wrapMode = TextureWrapMode.Clamp				
			};
			_simulationTexture.Create();

			SetupSimulation();
		}

		private void SetupSimulation()
		{
			Ctx.GrassSimulationComputeShader.SetInt("StartIndex", _startIndex);
			Ctx.GrassSimulationComputeShader.SetFloat("ParameterOffsetX", _parameterOffsetX);
			Ctx.GrassSimulationComputeShader.SetFloat("ParameterOffsetY", _parameterOffsetY);
			Ctx.GrassSimulationComputeShader.SetMatrix("PatchModelMatrix", _patchModelMatrix);

			//Set buffers for SimulationSetup Kernel
			Ctx.GrassSimulationComputeShader.SetTexture(Ctx.KernelSimulationSetup, "SimulationTexture", _simulationTexture);
			Ctx.GrassSimulationComputeShader.SetTexture(Ctx.KernelSimulationSetup, "NormalHeightTexture", _normalHeightTexture);

			uint threadGroupX, threadGroupY, threadGroupZ;
			Ctx.GrassSimulationComputeShader.GetKernelThreadGroupSizes(Ctx.KernelSimulationSetup, out threadGroupX,
				out threadGroupY, out threadGroupZ);

			//Run Physics Simulation
			Ctx.GrassSimulationComputeShader.Dispatch(Ctx.KernelSimulationSetup,
				(int) (Ctx.Settings.GrassDataResolution / threadGroupX), (int) (Ctx.Settings.GrassDataResolution / threadGroupY),
				1);
		}

		private void CreateDummyMesh()
		{
			var dummyMeshSize = Ctx.Settings.BillboardTextureGrassCount;
			var dummyVertices = new Vector3[dummyMeshSize];
			var indices = new int[dummyMeshSize];

			for (var i = 0; i < dummyMeshSize; i++)
			{
				dummyVertices[i] = Vector3.zero;
				indices[i] = i;
			}

			_dummyMesh = new Mesh {vertices = dummyVertices};
			_dummyMesh.SetIndices(indices, MeshTopology.Points, 0);
			_dummyMesh.RecalculateBounds();
		}
		
		public override void DrawGizmo()
		{
			base.DrawGizmo();
			Gizmos.color = new Color(0f, 1f, 0f, 0.8f);
			
			if(!_boundsTexture1 || !_boundsTexture2) return;

			for (int i = _startIndex; i < _startIndex + Ctx.Settings.BillboardTextureGrassCount; i++)
			{
				var localV0 = new Vector3(Ctx.GrassInstance.UvData[i].Position.x, 0, Ctx.GrassInstance.UvData[i].Position.y);
				var v0 = _patchModelMatrix.MultiplyPoint(localV0);
				var v1Sample = _boundsTexture1.GetPixelBilinear(localV0.x, localV0.z);
				var v2Sample = _boundsTexture2.GetPixelBilinear(localV0.x, localV0.z);
				var v1 = v0 + new Vector3(v1Sample.r, v1Sample.g, v1Sample.b);
				var v2 = v0 + new Vector3(v2Sample.r, v2Sample.g, v2Sample.b);

				Gizmos.DrawLine(v0, v1);
				Gizmos.DrawLine(v1, v2);
			}
		}
	}
}